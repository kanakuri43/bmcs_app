using bmcs_app.Application.Closing;
using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
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
/// 振込手数料差額の入力（TODO.md 7-3）はこの画面のスコープ外。既存伝票の訂正・取消（TODO.md 7-5）は
/// <see cref="UpdateAsync"/>／<see cref="CancelSlipAsync"/> が担う。
///
/// <see cref="UpdateAsync"/>は充当先の追加を許さない（変更できるのは入金日付・伝票摘要・各行の
/// 入金方法・入金先口座・行摘要・行の削除のみ）。候補判定（<see cref="BuildSalesLineCandidateQuery"/>／
/// <see cref="BuildDetailInvoiceCandidateQuery"/>）は自伝票自身の充当を除外する仕組みを持たない
/// サーバー側クエリのため、追加を許すと自伝票の充当状況によって候補条件やあるべき金額が変わる
/// ケースを扱う必要が生じる。同じ理由で、既存行の<see cref="DetailReceiptEntity.AllocatedAmount"/>は
/// 訂正時に再計算せず読込時の値をそのまま保持する（2026-09-15ユーザー確認）。
///
/// 編集ロック（<see cref="EvaluateEditLockAsync"/>）は月次締めのみを見る。`receipt`（締め入金）と
/// 異なり、`detail_invoice`（明細請求書）の金額は`sales`から都度導出され、`detail_receipt`からは
/// 導出されない（スナップショットを焼き込む処理が無い）ため、請求締めスナップショット相当の
/// ロック条件は不要（詳細は<see cref="ReceiptEntryService"/>のdoc comment）。
///
/// 詳細な設計判断（候補条件・保存時の検証・同時実行制御の考え方）は
/// <c>docs/design_document.md</c> 18・19章を参照。
/// </summary>
public class DetailReceiptEntryService(
    BmcsDbContext dbContext,
    SettlementService settlementService,
    SlipNumberService slipNumberService,
    MonthlyClosedService monthlyClosedService,
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

        var depositMethods = await ValidateLineFieldsAsync(lines, cancellationToken);

        var monthlyClosedReason = await monthlyClosedService.CheckEntryAsync(customerCode, receiptDate, "入金日付", cancellationToken);
        if (monthlyClosedReason is not null)
        {
            throw new DetailReceiptEntryException(monthlyClosedReason);
        }

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
                DepositMethodCode = line.DepositMethodCode,
                BankAccountCode = depositMethods[line.DepositMethodCode].RequiresBankAccount ? line.BankAccountCode : null,
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

        await settlementService.RecalculateForBillingGroupAsync(customer.CustomerCode, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "明細入金を登録しました。DetailReceiptNumber={DetailReceiptNumber} CustomerCode={CustomerCode} 行数={LineCount}",
            detailReceiptNumber, customer.CustomerCode, entities.Count);

        return detailReceiptNumber;
    }

    /// <summary>明細入金No.で1件取得する（読み取り専用表示用・訂正の読込元の両方に使う。保存しない）。</summary>
    public Task<List<DetailReceiptEntity>> GetByNumberAsync(
        string detailReceiptNumber, CancellationToken cancellationToken = default)
        => dbContext.DetailReceipts
            .AsNoTracking()
            .Where(r => r.DetailReceiptNumber == detailReceiptNumber && !r.IsDeleted)
            .OrderBy(r => r.LineNumber)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// 対象の`detail_receipt`行が訂正・取消不可かどうかを判定する（TODO.md 7-5）。月次締めのみを
    /// 見る（このクラスの doc comment を参照。`receipt`と異なり請求締めスナップショット相当は無い）。
    /// </summary>
    public async Task<SalesEditLock> EvaluateEditLockAsync(
        IReadOnlyList<DetailReceiptEntity> lines, CancellationToken cancellationToken = default)
    {
        var customerCode = lines[0].CustomerCode;
        var receiptDate = lines[0].ReceiptDate;

        var monthlyClosingConfirmed = await monthlyClosedService.IsClosedAsync(customerCode, receiptDate, cancellationToken);

        return monthlyClosingConfirmed
            ? new SalesEditLock(true, "月次締め済みのため訂正・取消できません。")
            : SalesEditLock.Unlocked;
    }

    /// <summary>
    /// 既存の明細入金伝票を訂正する（TODO.md 7-5）。元伝票の直接修正（C-6）であり、赤伝は発行しない。
    /// 充当先の追加はできない（このクラスの doc comment を参照）。変更できるのは入金日付・伝票摘要
    /// （伝票単位）、各行の入金方法・入金先口座・行摘要、および行の削除のみ。
    /// </summary>
    /// <param name="detailReceiptNumber">対象の明細入金伝票番号。</param>
    /// <param name="receiptDate">訂正後の入金日付（伝票単位の値）。</param>
    /// <param name="slipRemarks">訂正後の伝票摘要（伝票単位の値）。</param>
    /// <param name="lines">
    /// 保存後にあるべき明細行の集合。全て読込時に存在した
    /// <see cref="DetailReceiptLineCorrection.LineNumber"/>を指定すること（新規追加は不可）。
    /// 読込時にあった行番号のうち含まれないものは論理削除する。
    /// </param>
    /// <param name="loadedLineNumbers">画面が伝票を読み込んだ時点の明細行番号の集合。</param>
    /// <exception cref="DetailReceiptEntryException">
    /// 対象の明細入金が存在しない、編集ロックに該当する、充当先の追加を試みた、全行削除しようと
    /// した、または明細行の内容が不正な場合。
    /// </exception>
    /// <exception cref="SlipConcurrencyException">他のユーザーが同じ伝票を更新済みの場合。</exception>
    public async Task UpdateAsync(
        string detailReceiptNumber,
        DateOnly receiptDate,
        string? slipRemarks,
        IReadOnlyList<DetailReceiptLineCorrection> lines,
        IReadOnlyList<short> loadedLineNumbers,
        CancellationToken cancellationToken = default)
    {
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var currentLines = await dbContext.DetailReceipts
            .Where(r => r.DetailReceiptNumber == detailReceiptNumber && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        if (currentLines.Count == 0)
        {
            throw new DetailReceiptEntryException($"明細入金が見つかりません。DetailReceiptNumber={detailReceiptNumber}");
        }

        SlipConcurrencyGuard.EnsureLineSetUnchanged(
            loadedLineNumbers, currentLines.Select(l => l.LineNumber).ToList());

        var lockResultBefore = await EvaluateEditLockAsync(currentLines, cancellationToken);
        if (lockResultBefore.IsLocked)
        {
            throw new DetailReceiptEntryException(lockResultBefore.Reason!);
        }

        var currentByLineNumber = currentLines.ToDictionary(l => l.LineNumber);
        if (lines.Any(l => !currentByLineNumber.ContainsKey(l.LineNumber)))
        {
            throw new DetailReceiptEntryException("訂正で充当先を追加することはできません。別伝票で登録してください。");
        }

        var depositMethods = await ValidateLineFieldsForCorrectionAsync(lines, cancellationToken);

        var keptLineNumbers = lines.Select(l => l.LineNumber).ToHashSet();
        foreach (var current in currentLines.Where(l => !keptLineNumbers.Contains(l.LineNumber)))
        {
            current.IsDeleted = true;
        }

        foreach (var incoming in lines)
        {
            var current = currentByLineNumber[incoming.LineNumber];
            current.DepositMethodCode = incoming.DepositMethodCode;
            current.BankAccountCode = depositMethods[incoming.DepositMethodCode].RequiresBankAccount
                ? incoming.BankAccountCode : null;
            current.LineRemarks = incoming.LineRemarks;
        }

        var activeLines = currentLines.Where(l => !l.IsDeleted).ToList();
        if (activeLines.Count == 0)
        {
            throw new DetailReceiptEntryException("訂正で全行を削除することはできません。取消をご利用ください。");
        }

        // 伝票単位の値を生き残る全行へ反映する。receipt_amount は全行同値の不変条件
        // （DetailReceiptEntryService.SaveNewAsync 参照）を、行の一部だけを論理削除した後も
        // 維持する必要があるため、Σ AllocatedAmount を都度ここで書き直す
        // （AllocatedAmount 自体は再計算しない。このクラスの doc comment を参照）。
        var newReceiptAmount = activeLines.Sum(l => l.AllocatedAmount);
        foreach (var line in activeLines)
        {
            line.ReceiptDate = receiptDate;
            line.SlipRemarks = slipRemarks;
            line.ReceiptAmount = newReceiptAmount;
        }

        var lockResultAfter = await EvaluateEditLockAsync(activeLines, cancellationToken);
        if (lockResultAfter.IsLocked)
        {
            throw new DetailReceiptEntryException(
                $"訂正後の内容は編集ロック対象になるため保存できません: {lockResultAfter.Reason}");
        }

        SlipConcurrencyGuard.TouchAll(currentLines, employeeCode, now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SlipConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }
        catch (DbUpdateException ex)
        {
            throw new DetailReceiptEntryException("明細入金の保存に失敗しました。", ex);
        }

        await settlementService.RecalculateForBillingGroupAsync(currentLines[0].CustomerCode, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("明細入金を訂正しました。DetailReceiptNumber={DetailReceiptNumber}", detailReceiptNumber);
    }

    /// <summary>
    /// 明細入金伝票を取消する（TODO.md 7-5）。全明細行を論理削除する（M-17）。編集ロックに該当する
    /// 伝票は取消できない。
    /// </summary>
    /// <exception cref="DetailReceiptEntryException">対象の明細入金が存在しない、または編集ロックに該当する場合。</exception>
    /// <exception cref="SlipConcurrencyException">他のユーザーが同じ伝票を更新済みの場合。</exception>
    public async Task CancelSlipAsync(string detailReceiptNumber, CancellationToken cancellationToken = default)
    {
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var lines = await dbContext.DetailReceipts
            .Where(r => r.DetailReceiptNumber == detailReceiptNumber && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            throw new DetailReceiptEntryException($"明細入金が見つかりません。DetailReceiptNumber={detailReceiptNumber}");
        }

        var lockResult = await EvaluateEditLockAsync(lines, cancellationToken);
        if (lockResult.IsLocked)
        {
            throw new DetailReceiptEntryException(lockResult.Reason!);
        }

        foreach (var line in lines)
        {
            line.IsDeleted = true;
            line.UpdatedBy = employeeCode;
            line.UpdatedAt = now;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SlipConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        await settlementService.RecalculateForBillingGroupAsync(lines[0].CustomerCode, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "明細入金を取消しました。DetailReceiptNumber={DetailReceiptNumber} 行数={LineCount}",
            detailReceiptNumber, lines.Count);
    }

    /// <summary>
    /// 訂正で編集可能なフィールド（入金方法・入金先口座）のみを検証する。<see cref="ValidateLineFieldsAsync"/>
    /// と異なり充当先の妥当性は見ない（訂正では充当先を変更できないため）。手形除外の判定は
    /// <see cref="Domain.Entities.DepositMethod.RequiresBillDueDate"/>に基づく（旧enumから
    /// 2026-09-18にマスタ駆動へ移行。DBのCHECK制約では他テーブル参照ができないため、
    /// この検証がクロステーブル整合性の唯一の担保点になる）。
    /// </summary>
    private async Task<Dictionary<string, Domain.Entities.DepositMethod>> ValidateLineFieldsForCorrectionAsync(
        IReadOnlyList<DetailReceiptLineCorrection> lines, CancellationToken cancellationToken)
    {
        var depositMethods = await LoadDepositMethodsAsync(lines.Select(l => l.DepositMethodCode), cancellationToken);

        foreach (var line in lines)
        {
            var method = GetDepositMethodOrThrow(depositMethods, line.DepositMethodCode);
            ValidateDepositMethodForDetailReceipt(method, line.BankAccountCode);
        }

        return depositMethods;
    }

    private async Task<Dictionary<string, Domain.Entities.DepositMethod>> ValidateLineFieldsAsync(
        IReadOnlyList<DetailReceiptLineInput> lines, CancellationToken cancellationToken)
    {
        var depositMethods = await LoadDepositMethodsAsync(lines.Select(l => l.DepositMethodCode), cancellationToken);

        foreach (var line in lines)
        {
            var method = GetDepositMethodOrThrow(depositMethods, line.DepositMethodCode);
            ValidateDepositMethodForDetailReceipt(method, line.BankAccountCode);

            var isSalesTarget = line.TargetType == DetailReceiptTargetType.SalesLine;
            var salesTargetValid = line.TargetSalesSlipNumber is not null && line.TargetSalesLineNumber is not null;
            var invoiceTargetValid = !string.IsNullOrWhiteSpace(line.TargetDetailInvoiceNumber);

            if ((isSalesTarget && !salesTargetValid) || (!isSalesTarget && !invoiceTargetValid))
            {
                throw new DetailReceiptEntryException("充当先の指定が不正です。");
            }
        }

        return depositMethods;
    }

    private Task<Dictionary<string, Domain.Entities.DepositMethod>> LoadDepositMethodsAsync(
        IEnumerable<string> depositMethodCodes, CancellationToken cancellationToken)
    {
        var codes = depositMethodCodes.Distinct().ToList();
        return dbContext.DepositMethods
            .AsNoTracking()
            .Where(m => codes.Contains(m.DepositMethodCode))
            .ToDictionaryAsync(m => m.DepositMethodCode, cancellationToken);
    }

    private static Domain.Entities.DepositMethod GetDepositMethodOrThrow(
        IReadOnlyDictionary<string, Domain.Entities.DepositMethod> depositMethods, string depositMethodCode)
        => depositMethods.TryGetValue(depositMethodCode, out var method)
            ? method
            : throw new DetailReceiptEntryException($"入金方法「{depositMethodCode}」は見つかりません。");

    /// <summary>この画面（detail_receipt）は手形期日を保持する列を持たないため、手形系の入金方法は使えない。</summary>
    private static void ValidateDepositMethodForDetailReceipt(Domain.Entities.DepositMethod method, string? bankAccountCode)
    {
        if (method.RequiresBillDueDate)
        {
            throw new DetailReceiptEntryException(
                $"この画面では入金方法「{method.DepositMethodName}」は指定できません（明細入金は手形期日を保持する列を持ちません）。");
        }

        if (method.RequiresBankAccount && string.IsNullOrWhiteSpace(bankAccountCode))
        {
            throw new DetailReceiptEntryException($"{method.DepositMethodName}の行は入金先口座を指定してください。");
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
    string DepositMethodCode,
    string? BankAccountCode,
    string? LineRemarks);

/// <summary>
/// 訂正（<see cref="DetailReceiptEntryService.UpdateAsync"/>）用の明細行の入力。充当先
/// （<see cref="DetailReceiptLineInput.TargetType"/>等）を持たない＝設計上、訂正で充当先を
/// 変更できないことをシグネチャで表す（このクラスの doc comment を参照）。
/// </summary>
public sealed record DetailReceiptLineCorrection(
    short LineNumber,
    string DepositMethodCode,
    string? BankAccountCode,
    string? LineRemarks);

/// <summary>明細入金入力の業務ルール違反。</summary>
public sealed class DetailReceiptEntryException(string message, Exception? inner = null) : Exception(message, inner);
