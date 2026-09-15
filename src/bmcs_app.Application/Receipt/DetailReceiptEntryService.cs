using bmcs_app.Application.Common;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using DetailInvoiceEntity = bmcs_app.Domain.Entities.DetailInvoice;
using DetailReceiptEntity = bmcs_app.Domain.Entities.DetailReceipt;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Receipt;

/// <summary>
/// 明細入金画面（TODO.md 7-4）のユースケース。都度得意先（<see cref="TaxUnit.Line"/>）専用。
/// 締め得意先（請求単位／伝票単位）の入金は入金入力画面（TODO.md 7-2、<see cref="ReceiptEntryService"/>）
/// が担う。
///
/// 充当先は2種類（<see cref="DetailReceiptTargetType"/>）で、粒度が異なる: 売上明細行を直接指定
/// （<see cref="DetailReceiptTargetType.SalesLine"/>）は行単位、明細請求書を指定
/// （<see cref="DetailReceiptTargetType.DetailInvoice"/>）は請求書まるごと1行（2026-09-15
/// ユーザー確認。デモ<c>bmcs_app.LineReceipt</c>は請求書タブでも行単位だったが、
/// <c>docs/database-schema.md</c> 2.11節のスキーマに合わせた）。
///
/// 金額（<see cref="DetailReceiptEntity.AllocatedAmount"/>）は常に対象の全額（または残額）を
/// 充当し、手入力では変更できない。前受金は無く、充当先が未定の行は作らない。
/// 振込手数料差額の入力（TODO.md 7-3）・既存伝票の訂正／取消（TODO.md 7-5）はこの画面の
/// スコープ外。実装範囲は新規登録と、既存伝票番号による読み取り専用の読込のみ。
///
/// 詳細な設計判断（候補条件・保存時の検証・同時実行制御の考え方）は
/// <c>docs/design_document.md</c> 18章を参照。
/// </summary>
public class DetailReceiptEntryService(
    BmcsDbContext dbContext,
    SettlementService settlementService,
    SlipNumberService slipNumberService,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<DetailReceiptEntryService> logger)
{
    /// <summary>
    /// 売上伝票タブの候補（画面表示用、保存しない）。未消込・一部消込の両方を含む
    /// （一部消込済みの行を候補から外すと残額を永久に入金できなくなるため）。
    /// 表示・取込金額は残額（<c>amount - settled_amount</c>）とする。
    /// </summary>
    public async Task<IReadOnlyList<DetailReceiptSalesCandidate>> GetSalesLineCandidatesAsync(
        string customerCode, CancellationToken cancellationToken = default)
    {
        var lines = await BuildSalesLineCandidateQuery(customerCode, tracking: false)
            .OrderBy(s => s.SlipDate).ThenBy(s => s.SalesSlipNumber).ThenBy(s => s.LineNumber)
            .ToListAsync(cancellationToken);

        return lines.Select(ToSalesCandidate).ToList();
    }

    /// <summary>
    /// 明細請求書タブの候補（画面表示用、保存しない）。連携先の売上明細行がすべて未消込の
    /// 発行済み明細請求書のみを返す（一部でも消込済みの行が含まれると全額充当で過充当になるため）。
    /// </summary>
    public async Task<IReadOnlyList<DetailReceiptInvoiceCandidate>> GetDetailInvoiceCandidatesAsync(
        string customerCode, CancellationToken cancellationToken = default)
    {
        var invoices = await BuildDetailInvoiceCandidateQuery(customerCode, tracking: false)
            .OrderBy(d => d.IssueDate).ThenBy(d => d.DetailInvoiceNumber)
            .ToListAsync(cancellationToken);

        var invoiceNumbers = invoices.Select(d => d.DetailInvoiceNumber).ToList();
        var lineCounts = await dbContext.DetailInvoiceSalesLines
            .AsNoTracking()
            .Where(l => invoiceNumbers.Contains(l.DetailInvoiceNumber))
            .GroupBy(l => l.DetailInvoiceNumber)
            .Select(g => new { DetailInvoiceNumber = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DetailInvoiceNumber, x => x.Count, cancellationToken);

        return invoices
            .Select(d => new DetailReceiptInvoiceCandidate(
                d.DetailInvoiceNumber, d.IssueDate, d.AddresseeName,
                lineCounts.GetValueOrDefault(d.DetailInvoiceNumber), d.TotalAmount))
            .ToList();
    }

    /// <summary>
    /// 新規に明細入金伝票を登録する。<paramref name="lines"/>の金額は受け取らない
    /// （利用者が手入力できないため）。対象の現在の残額・請求書合計額をトランザクション内で
    /// 再取得し、サーバー側で確定する。
    /// </summary>
    /// <exception cref="DetailReceiptEntryException">
    /// 明細が0件、充当先の重複、二重充当（請求書とその連携売上明細行の同時指定）、得意先が
    /// 対象外（都度得意先以外・存在しない・無効化済み）、振込の行で入金先口座が未指定、手形が
    /// 指定された、候補条件を満たさない対象が含まれる、明細請求書の金額と連携先売上合計が
    /// 一致しない、または入金額の合計が0以下の場合。
    /// </exception>
    public async Task<string> SaveNewAsync(
        string customerCode,
        DateOnly receiptDate,
        string? slipRemarks,
        IReadOnlyList<DetailReceiptLineInput> lines,
        CancellationToken cancellationToken = default)
    {
        if (lines.Count == 0)
        {
            throw new DetailReceiptEntryException("明細行を1件以上入力してください。");
        }

        ValidateLineFields(lines);

        var salesKeys = lines
            .Where(l => l.TargetType == DetailReceiptTargetType.SalesLine)
            .Select(l => (SlipNumber: l.TargetSalesSlipNumber!, LineNumber: l.TargetSalesLineNumber!.Value))
            .ToList();
        if (salesKeys.Count != salesKeys.Distinct().Count())
        {
            throw new DetailReceiptEntryException("同じ売上明細行が重複して指定されています。");
        }

        var invoiceNumbers = lines
            .Where(l => l.TargetType == DetailReceiptTargetType.DetailInvoice)
            .Select(l => l.TargetDetailInvoiceNumber!)
            .ToList();
        if (invoiceNumbers.Count != invoiceNumbers.Distinct().Count())
        {
            throw new DetailReceiptEntryException("同じ明細請求書が重複して指定されています。");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var customer = await GetLineCustomerAsync(customerCode, cancellationToken);

        // 二重充当の防止: 選択した明細請求書に連携している売上明細行が、同じ伝票内で
        // 直接指定としても選ばれていないか（docs/database-schema.md 2.11節、両方が同時に
        // 効きうる仕様だが、同一伝票内での重複指定は利用者の入力ミスとして拒否する）。
        if (invoiceNumbers.Count > 0 && salesKeys.Count > 0)
        {
            var linkedBySelectedInvoices = await dbContext.DetailInvoiceSalesLines
                .AsNoTracking()
                .Where(l => invoiceNumbers.Contains(l.DetailInvoiceNumber))
                .Select(l => new { l.SalesSlipNumber, l.SalesLineNumber })
                .ToListAsync(cancellationToken);

            if (salesKeys.Any(k => linkedBySelectedInvoices.Any(
                    l => l.SalesSlipNumber == k.SlipNumber && l.SalesLineNumber == k.LineNumber)))
            {
                throw new DetailReceiptEntryException(
                    "同じ売上明細行が、明細請求書経由と直接指定の両方で重複して指定されています。");
            }
        }

        // 候補条件をトランザクション内で再実行し、金額（残額・請求書合計額）を利用者の入力に
        // 頼らずサーバー側で確定する（docs/architecture.md 9章、6-3 IssueAsync と同じ方式）。
        var candidateSales = await BuildSalesLineCandidateQuery(customer.CustomerCode, tracking: true)
            .ToDictionaryAsync(s => (s.SalesSlipNumber, s.LineNumber), cancellationToken);
        var candidateInvoices = await BuildDetailInvoiceCandidateQuery(customer.CustomerCode, tracking: true)
            .ToDictionaryAsync(d => d.DetailInvoiceNumber, cancellationToken);

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        var detailReceiptNumber = await slipNumberService.NextAsync(SlipNumberKind.DetailReceipt, cancellationToken);

        var entities = new List<DetailReceiptEntity>(lines.Count);
        short lineNumber = 1;

        foreach (var line in lines)
        {
            decimal allocatedAmount;

            if (line.TargetType == DetailReceiptTargetType.SalesLine)
            {
                var key = (line.TargetSalesSlipNumber!, line.TargetSalesLineNumber!.Value);
                if (!candidateSales.TryGetValue(key, out var sales))
                {
                    throw new DetailReceiptEntryException(
                        "対象条件を満たさない売上明細行が含まれています（既に消込完了・金額ゼロ・" +
                        $"削除済みのいずれか）。SalesSlipNumber={key.Item1} LineNumber={key.Item2}");
                }

                allocatedAmount = sales.Amount - sales.SettledAmount;
            }
            else
            {
                var invoiceNumber = line.TargetDetailInvoiceNumber!;
                if (!candidateInvoices.TryGetValue(invoiceNumber, out var invoice))
                {
                    throw new DetailReceiptEntryException(
                        "対象条件を満たさない明細請求書が含まれています（取消済み・金額ゼロ・既に" +
                        $"入金充当済み・連携行に消込済みが含まれるのいずれか）。DetailInvoiceNumber={invoiceNumber}");
                }

                // 修正点6: 発行時点の計算式（DetailInvoiceService.IssueAsync）からは常に一致する
                // はずだが、データ補正等でズレた場合に自動で丸めず拒否する（同サービスの税額照合と
                // 同じ考え方）。
                var linkedSalesTotal = await dbContext.DetailInvoiceSalesLines
                    .AsNoTracking()
                    .Where(l => l.DetailInvoiceNumber == invoiceNumber)
                    .Join(dbContext.Sales, l => new { l.SalesSlipNumber, l.SalesLineNumber },
                        s => new { s.SalesSlipNumber, SalesLineNumber = s.LineNumber }, (l, s) => s.Amount)
                    .SumAsync(cancellationToken);
                if (linkedSalesTotal != invoice.TotalAmount)
                {
                    throw new DetailReceiptEntryException(
                        "明細請求書の金額が連携先売上明細行の合計と一致しません。" +
                        $"DetailInvoiceNumber={invoiceNumber} 請求書金額={invoice.TotalAmount} 売上合計={linkedSalesTotal}");
                }

                allocatedAmount = invoice.TotalAmount;
            }

            entities.Add(new DetailReceiptEntity
            {
                DetailReceiptNumber = detailReceiptNumber,
                LineNumber = lineNumber++,
                ReceiptDate = receiptDate,
                CustomerCode = customer.CustomerCode,
                CustomerName = customer.CustomerName,
                ReceiptMethod = line.ReceiptMethod,
                BankAccountCode = line.ReceiptMethod == ReceiptMethod.BankTransfer ? line.BankAccountCode : null,
                ReceiptAmount = 0m, // 全行の合計が確定してから一括で設定する
                TargetType = line.TargetType,
                TargetSalesSlipNumber = line.TargetType == DetailReceiptTargetType.SalesLine
                    ? line.TargetSalesSlipNumber : null,
                TargetSalesLineNumber = line.TargetType == DetailReceiptTargetType.SalesLine
                    ? line.TargetSalesLineNumber : null,
                TargetDetailInvoiceNumber = line.TargetType == DetailReceiptTargetType.DetailInvoice
                    ? line.TargetDetailInvoiceNumber : null,
                AllocatedAmount = allocatedAmount,
                FeeAdjustmentAmount = 0m,
                AllocationStatus = AllocationStatus.Unallocated,
                SlipRemarks = slipRemarks,
                LineRemarks = line.LineRemarks,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
            });
        }

        // receipt_amount は伝票単位の値（全行同値）。SettlementService.RecalculateDetailAsync が
        // slipLines[0] の receipt_amount のみを参照するため、この不変条件を必ず保つこと
        // （docs/design_document.md 18章。Phase 7-5で行の一部だけを論理削除する場合は要再検証）。
        var receiptAmount = entities.Sum(e => e.AllocatedAmount);
        if (receiptAmount <= 0m)
        {
            throw new DetailReceiptEntryException("入金額の合計は0より大きい値を入力してください。");
        }

        foreach (var entity in entities)
        {
            entity.ReceiptAmount = receiptAmount;
        }

        dbContext.DetailReceipts.AddRange(entities);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new DetailReceiptEntryException("入金の保存に失敗しました。", ex);
        }

        await settlementService.RecalculateForCustomerAsync(customer.CustomerCode, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "明細入金を登録しました。DetailReceiptNumber={DetailReceiptNumber} CustomerCode={CustomerCode} 行数={LineCount}",
            detailReceiptNumber, customer.CustomerCode, entities.Count);

        return detailReceiptNumber;
    }

    /// <summary>明細入金No.で1件取得する（読み取り専用表示用。保存しない）。訂正・取消はTODO.md 7-5。</summary>
    public Task<List<DetailReceiptEntity>> GetByNumberAsync(
        string detailReceiptNumber, CancellationToken cancellationToken = default)
        => dbContext.DetailReceipts
            .AsNoTracking()
            .Where(r => r.DetailReceiptNumber == detailReceiptNumber && !r.IsDeleted)
            .OrderBy(r => r.LineNumber)
            .ToListAsync(cancellationToken);

    private static void ValidateLineFields(IReadOnlyList<DetailReceiptLineInput> lines)
    {
        foreach (var line in lines)
        {
            if (line.ReceiptMethod == ReceiptMethod.PromissoryNote)
            {
                throw new DetailReceiptEntryException(
                    "この画面では入金方法に手形を指定できません（明細入金は手形期日を保持する列を持ちません）。");
            }

            if (line.ReceiptMethod == ReceiptMethod.BankTransfer && string.IsNullOrWhiteSpace(line.BankAccountCode))
            {
                throw new DetailReceiptEntryException("振込の行は入金先口座を指定してください。");
            }

            var isSalesTarget = line.TargetType == DetailReceiptTargetType.SalesLine;
            var salesTargetValid = line.TargetSalesSlipNumber is not null && line.TargetSalesLineNumber is not null;
            var invoiceTargetValid = !string.IsNullOrWhiteSpace(line.TargetDetailInvoiceNumber);

            if ((isSalesTarget && !salesTargetValid) || (!isSalesTarget && !invoiceTargetValid))
            {
                throw new DetailReceiptEntryException("充当先の指定が不正です。");
            }
        }
    }

    private async Task<CustomerEntity> GetLineCustomerAsync(string customerCode, CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerCode == customerCode && !c.IsDeleted, cancellationToken)
            ?? throw new DetailReceiptEntryException($"得意先コード「{customerCode}」が見つかりません。");

        if (customer.TaxUnit != TaxUnit.Line)
        {
            throw new DetailReceiptEntryException(
                "締め得意先（請求単位／伝票単位）はこの画面では入金登録できません。入金入力画面をご利用ください。");
        }

        return customer;
    }

    /// <summary>
    /// 売上伝票タブの対象条件: 都度得意先・未削除・消込完了でない（未消込／一部消込の両方を含む）・
    /// 金額ゼロでない。明細請求書への連携有無は問わない（一部だけ入金があるケースを直接指定で
    /// 救うため。docs/design_document.md 18章）。
    /// </summary>
    private IQueryable<SalesEntity> BuildSalesLineCandidateQuery(string customerCode, bool tracking)
    {
        var query = dbContext.Sales
            .Where(s => s.CustomerCode == customerCode
                && s.TaxUnit == TaxUnit.Line
                && !s.IsDeleted
                && s.SettlementStatus != SettlementStatus.FullySettled
                && s.Amount != 0m);

        return tracking ? query : query.AsNoTracking();
    }

    /// <summary>
    /// 明細請求書タブの対象条件: 未削除・発行済み・金額ゼロでない・この請求書を指す未削除の
    /// 明細入金が存在しない・連携先の売上明細行がすべて未消込。
    /// </summary>
    private IQueryable<DetailInvoiceEntity> BuildDetailInvoiceCandidateQuery(string customerCode, bool tracking)
    {
        var query = dbContext.DetailInvoices
            .Where(d => d.CustomerCode == customerCode
                && !d.IsDeleted
                && d.InvoiceStatus == DetailInvoiceStatus.Issued
                && d.TotalAmount != 0m
                && !dbContext.DetailReceipts.Any(r => r.TargetDetailInvoiceNumber == d.DetailInvoiceNumber && !r.IsDeleted)
                && !dbContext.DetailInvoiceSalesLines
                    .Where(l => l.DetailInvoiceNumber == d.DetailInvoiceNumber)
                    .Any(l => dbContext.Sales.Any(s =>
                        s.SalesSlipNumber == l.SalesSlipNumber
                        && s.LineNumber == l.SalesLineNumber
                        && s.SettlementStatus != SettlementStatus.Unsettled)));

        return tracking ? query : query.AsNoTracking();
    }

    private static DetailReceiptSalesCandidate ToSalesCandidate(SalesEntity s) => new(
        s.SalesSlipNumber,
        s.LineNumber,
        s.SlipDate,
        s.SlipType,
        s.ProductCode,
        s.ProductName,
        s.Quantity,
        s.UnitPrice,
        s.Amount,
        s.SettledAmount,
        s.Amount - s.SettledAmount,
        s.LineRemarks);
}

/// <summary>売上伝票タブの候補1行（画面表示用）。</summary>
public sealed record DetailReceiptSalesCandidate(
    string SalesSlipNumber,
    short LineNumber,
    DateOnly SlipDate,
    SlipType SlipType,
    string ProductCode,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount,
    decimal SettledAmount,
    decimal RemainingAmount,
    string? LineRemarks);

/// <summary>明細請求書タブの候補1件（画面表示用）。</summary>
public sealed record DetailReceiptInvoiceCandidate(
    string DetailInvoiceNumber,
    DateOnly IssueDate,
    string AddresseeName,
    int LineCount,
    decimal TotalAmount);

/// <summary>明細入金明細行の入力。充当先はどちらか一方のみ指定する。</summary>
public sealed record DetailReceiptLineInput(
    DetailReceiptTargetType TargetType,
    string? TargetSalesSlipNumber,
    short? TargetSalesLineNumber,
    string? TargetDetailInvoiceNumber,
    ReceiptMethod ReceiptMethod,
    string? BankAccountCode,
    string? LineRemarks);

/// <summary>明細入金入力の業務ルール違反。</summary>
public sealed class DetailReceiptEntryException(string message, Exception? inner = null) : Exception(message, inner);
