using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using DetailReceiptEntity = bmcs_app.Domain.Entities.DetailReceipt;
using ReceiptEntity = bmcs_app.Domain.Entities.Receipt;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Receipt;

/// <summary>
/// 入金消込のユースケース（TODO.md 7-1）。得意先単位で、売上明細行の消込キャッシュ列
/// （<see cref="SalesEntity.SettlementStatus"/>／<see cref="SalesEntity.SettledAmount"/>）と
/// 入金伝票の充当キャッシュ列（<see cref="ReceiptEntity.AllocationStatus"/>／
/// <see cref="DetailReceiptEntity.AllocationStatus"/>）を、入金明細（<c>receipt</c>／
/// <c>detail_receipt</c>）から毎回全件再計算して書き戻す（差分方式ではない。完了条件が
/// 「登録・取消・訂正のいずれでもキャッシュ列が実態と一致する」であり、差分計算は
/// ドリフトを許すため。詳細は docs/design_document.md 16章）。
///
/// スコープを「伝票」ではなく「得意先」にしているのは、訂正で充当先（<c>billing_number</c>等）
/// が変わった場合に、変更前・変更後の両方を自動的に再計算できるようにするため
/// （<c>customer_code</c>は伝票単位の値で訂正でも変わらない）。
///
/// 呼び出し元（入金の登録・取消・訂正、締め解除、売上の訂正・取消）が入金明細・売上明細を
/// 保存した直後に、同一の明示トランザクション内で本サービスを呼ぶ。本サービス自身は
/// トランザクションを開始・コミットしない（<c>docs/architecture.md</c> 6章。
/// <see cref="bmcs_app.Application.Order.OrderStatusService"/>と同じ構成）。
/// </summary>
public class SettlementService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<SettlementService> logger)
{
    /// <summary>指定した得意先の売上明細行・入金明細行のキャッシュ列を再計算する。</summary>
    /// <exception cref="InvalidOperationException">明示トランザクションが開始されていない場合。</exception>
    /// <exception cref="SettlementException">得意先が存在しない、または保存に失敗した場合。</exception>
    /// <exception cref="SlipConcurrencyException">他のユーザーが対象行を更新していた場合。</exception>
    public async Task<SettlementRecalculationResult> RecalculateForCustomerAsync(
        string customerCode, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "消込の再計算は、入金・売上の登録・取消・訂正と同一の明示トランザクション内で実行してください" +
                "（dbContext.Database.BeginTransactionAsync() を先に呼び出す）。" +
                "docs/architecture.md 6章を参照。");
        }

        // 無効化済み得意先の伝票も再計算対象になるため IsDeleted で絞らない。
        var customer = await dbContext.Customers
            .FirstOrDefaultAsync(c => c.CustomerCode == customerCode, cancellationToken)
            ?? throw new SettlementException($"得意先が見つかりません。CustomerCode={customerCode}");

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        var result = customer.TaxUnit == TaxUnit.Line
            ? await RecalculateDetailAsync(customerCode, employeeCode, now, cancellationToken)
            : await RecalculateClosingAsync(customerCode, employeeCode, now, cancellationToken);

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
            throw new SettlementException("消込結果の保存に失敗しました。", ex);
        }

        logger.LogInformation(
            "消込を再計算しました。CustomerCode={CustomerCode} 更新売上行数={SalesLineCount} 更新入金行数={ReceiptLineCount}",
            customerCode, result.UpdatedSalesLineCount, result.UpdatedReceiptLineCount);

        return result;
    }

    /// <summary>
    /// 締め得意先（<see cref="TaxUnit.Invoice"/>／<see cref="TaxUnit.Slip"/>）。
    /// <c>receipt</c>が<c>billing_number</c>へ充当した額を、その<c>billing_number</c>を持つ
    /// 売上明細行へ伝票日付→伝票番号→行番号の古い順に配分する。<c>billing_number</c>がNULLの
    /// 行（未請求）はプール0（未消込）になる。
    /// </summary>
    private async Task<SettlementRecalculationResult> RecalculateClosingAsync(
        string customerCode, string employeeCode, DateTime now, CancellationToken cancellationToken)
    {
        var salesLines = await dbContext.Sales
            .Where(s => s.CustomerCode == customerCode && !s.IsDeleted)
            .ToListAsync(cancellationToken);

        var receiptLines = await dbContext.Receipts
            .Where(r => r.CustomerCode == customerCode && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        var allocationLines = await dbContext.ReceiptAllocations
            .Where(a => a.CustomerCode == customerCode && !a.IsDeleted)
            .ToListAsync(cancellationToken);

        var poolByBilling = allocationLines
            .Where(a => a.BillingNumber is not null)
            .GroupBy(a => a.BillingNumber!)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AllocatedAmount + a.FeeAdjustmentAmount));

        var updatedSalesCount = 0;

        foreach (var group in salesLines.GroupBy(s => s.BillingNumber))
        {
            var pool = group.Key is not null && poolByBilling.TryGetValue(group.Key, out var p) ? p : 0m;
            var ordered = group
                .OrderBy(s => s.SlipDate).ThenBy(s => s.SalesSlipNumber).ThenBy(s => s.LineNumber)
                .ToList();
            var allocations = SettlementAllocator.Allocate(ordered.Select(s => s.Amount).ToList(), pool);

            for (var i = 0; i < ordered.Count; i++)
            {
                updatedSalesCount += ApplySettlement(ordered[i], allocations[i], employeeCode, now);
            }
        }

        var allocatedTotalBySlip = allocationLines
            .GroupBy(a => a.ReceiptSlipNumber)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AllocatedAmount));

        var updatedReceiptCount = 0;
        foreach (var group in receiptLines.GroupBy(r => r.ReceiptSlipNumber))
        {
            var slipLines = group.ToList();
            var status = AllocationStatusCalculator.Determine(
                slipLines.Sum(r => r.Amount), allocatedTotalBySlip.GetValueOrDefault(group.Key));
            foreach (var line in slipLines)
            {
                updatedReceiptCount += ApplyReceiptAllocationStatus(line, status, employeeCode, now);
            }
        }

        return new SettlementRecalculationResult(updatedSalesCount, updatedReceiptCount);
    }

    /// <summary>
    /// 都度得意先（<see cref="TaxUnit.Line"/>）。<c>detail_receipt</c>の充当先は2種類あり、
    /// 売上明細行の直接指定（<see cref="DetailReceiptTargetType.SalesLine"/>）を先に確定し、
    /// 残額（<c>amount - 直接充当額</c>）を明細請求書経由（<see cref="DetailReceiptTargetType.DetailInvoice"/>）
    /// の配分に回す（名指しした指示を導出より優先する）。1つの売上明細行に両方が同時に
    /// 効くことはある（<c>docs/database-schema.md</c> 2.11節）。
    /// </summary>
    private async Task<SettlementRecalculationResult> RecalculateDetailAsync(
        string customerCode, string employeeCode, DateTime now, CancellationToken cancellationToken)
    {
        var salesLines = await dbContext.Sales
            .Where(s => s.CustomerCode == customerCode && s.TaxUnit == TaxUnit.Line && !s.IsDeleted)
            .ToListAsync(cancellationToken);

        var detailReceiptLines = await dbContext.DetailReceipts
            .Where(r => r.CustomerCode == customerCode && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        var slipNumbers = salesLines.Select(s => s.SalesSlipNumber).Distinct().ToList();
        var links = await dbContext.DetailInvoiceSalesLines
            .AsNoTracking()
            .Where(l => slipNumbers.Contains(l.SalesSlipNumber))
            .ToListAsync(cancellationToken);
        var invoiceByLine = links.ToDictionary(l => (l.SalesSlipNumber, l.SalesLineNumber), l => l.DetailInvoiceNumber);

        var directByLine = detailReceiptLines
            .Where(r => r.TargetType == DetailReceiptTargetType.SalesLine)
            .GroupBy(r => (r.TargetSalesSlipNumber!, r.TargetSalesLineNumber!.Value))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.AllocatedAmount + r.FeeAdjustmentAmount));

        var poolByInvoice = detailReceiptLines
            .Where(r => r.TargetType == DetailReceiptTargetType.DetailInvoice)
            .GroupBy(r => r.TargetDetailInvoiceNumber!)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.AllocatedAmount + r.FeeAdjustmentAmount));

        // 直接指定分を先に確定する（対象額=amountで頭打ち。単一要素リストで Allocate を再利用）。
        var directAllocated = new Dictionary<(string SalesSlipNumber, short LineNumber), decimal>();
        foreach (var line in salesLines)
        {
            var key = (line.SalesSlipNumber, line.LineNumber);
            directAllocated[key] = directByLine.TryGetValue(key, out var directPool)
                ? SettlementAllocator.Allocate([line.Amount], directPool)[0]
                : 0m;
        }

        var updatedSalesCount = 0;

        // 明細請求書に属さない行（直接指定のみ、または全く未消込）。
        foreach (var line in salesLines.Where(s => !invoiceByLine.ContainsKey((s.SalesSlipNumber, s.LineNumber))))
        {
            var settled = directAllocated[(line.SalesSlipNumber, line.LineNumber)];
            updatedSalesCount += ApplySettlement(line, settled, employeeCode, now);
        }

        // 明細請求書に属する行は、請求書ごとに残額(amount-直接充当額)を配分する。
        foreach (var invoiceGroup in salesLines
                     .Where(s => invoiceByLine.ContainsKey((s.SalesSlipNumber, s.LineNumber)))
                     .GroupBy(s => invoiceByLine[(s.SalesSlipNumber, s.LineNumber)]))
        {
            var pool = poolByInvoice.TryGetValue(invoiceGroup.Key, out var p) ? p : 0m;
            var ordered = invoiceGroup
                .OrderBy(s => s.SlipDate).ThenBy(s => s.SalesSlipNumber).ThenBy(s => s.LineNumber)
                .ToList();
            var remainingTargets = ordered
                .Select(s => s.Amount - directAllocated[(s.SalesSlipNumber, s.LineNumber)])
                .ToList();
            var invoiceShare = SettlementAllocator.Allocate(remainingTargets, pool);

            for (var i = 0; i < ordered.Count; i++)
            {
                var settled = directAllocated[(ordered[i].SalesSlipNumber, ordered[i].LineNumber)] + invoiceShare[i];
                updatedSalesCount += ApplySettlement(ordered[i], settled, employeeCode, now);
            }
        }

        var updatedReceiptCount = 0;
        foreach (var group in detailReceiptLines.GroupBy(r => r.DetailReceiptNumber))
        {
            var slipLines = group.ToList();
            var status = AllocationStatusCalculator.Determine(
                slipLines[0].ReceiptAmount, slipLines.Sum(r => r.AllocatedAmount));
            foreach (var line in slipLines)
            {
                updatedReceiptCount += ApplyDetailReceiptAllocationStatus(line, status, employeeCode, now);
            }
        }

        return new SettlementRecalculationResult(updatedSalesCount, updatedReceiptCount);
    }

    /// <summary>
    /// 値が実際に変わった場合だけ更新する（<see cref="Common.SlipConcurrencyGuard.TouchAll"/>とは
    /// 逆方針。再計算は得意先の全行に及ぶ派生更新であり、無関係な行まで rowversion 照合対象にすると
    /// 別の伝票を編集中の利用者を不要に弾くため。docs/architecture.md 9章）。
    /// </summary>
    private static int ApplySettlement(SalesEntity line, decimal settled, string employeeCode, DateTime now)
    {
        var status = SettlementStatusCalculator.Determine(line.Amount, settled);
        if (line.SettledAmount == settled && line.SettlementStatus == status)
        {
            return 0;
        }

        line.SettledAmount = settled;
        line.SettlementStatus = status;
        line.UpdatedBy = employeeCode;
        line.UpdatedAt = now;
        return 1;
    }

    private static int ApplyReceiptAllocationStatus(
        ReceiptEntity line, AllocationStatus status, string employeeCode, DateTime now)
    {
        if (line.AllocationStatus == status)
        {
            return 0;
        }

        line.AllocationStatus = status;
        line.UpdatedBy = employeeCode;
        line.UpdatedAt = now;
        return 1;
    }

    private static int ApplyDetailReceiptAllocationStatus(
        DetailReceiptEntity line, AllocationStatus status, string employeeCode, DateTime now)
    {
        if (line.AllocationStatus == status)
        {
            return 0;
        }

        line.AllocationStatus = status;
        line.UpdatedBy = employeeCode;
        line.UpdatedAt = now;
        return 1;
    }
}

/// <summary>消込再計算の業務ルール違反・データ異常。</summary>
public sealed class SettlementException(string message, Exception? inner = null) : Exception(message, inner);
