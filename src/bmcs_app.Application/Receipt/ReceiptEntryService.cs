using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using ReceiptAllocationEntity = bmcs_app.Domain.Entities.ReceiptAllocation;
using ReceiptEntity = bmcs_app.Domain.Entities.Receipt;

namespace bmcs_app.Application.Receipt;

/// <summary>
/// 入金入力画面（TODO.md 7-2）のユースケース。締め得意先（<see cref="TaxUnit.Invoice"/>／
/// <see cref="TaxUnit.Slip"/>）専用。都度得意先（<see cref="TaxUnit.Line"/>）の入金は
/// 明細入金画面（TODO.md 7-4）が担う。
///
/// 明細行は支払手段の内訳（入金方法＋金額）であり、利用者が直接入力する
/// （docs/design_document.md 17章、2026-09-15改訂）。請求への充当は、明細行の合計額を
/// 確定済み<c>billing</c>の未消込残額へ古い順（<c>billing_date</c>→<c>billing_number</c>）に
/// 自動配分して<see cref="ReceiptAllocationEntity"/>として保存する内部データであり、画面には
/// 表示しない（利用者にとって重要なのは充当先ではなく残高のため）。配分そのものは新規の
/// Domainクラスを作らず、7-1で実装済みの<see cref="SettlementAllocator"/>をそのまま流用する。
/// 全額を割り当てきれない残額（前受・過入金）は<c>billing_number = NULL</c>の1行にまとめる。
///
/// 振込手数料差額の入力（TODO.md 7-3）・既存伝票の訂正／取消（TODO.md 7-5）はこの画面の
/// スコープ外（別タスク）。この画面は新規登録と、既存伝票番号による読み取り専用の読込のみを扱う。
/// </summary>
public class ReceiptEntryService(
    BmcsDbContext dbContext,
    SettlementService settlementService,
    SlipNumberService slipNumberService,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<ReceiptEntryService> logger)
{
    /// <summary>
    /// 得意先の請求残高を返す（画面の「請求残高」表示用。保存しない）。
    /// </summary>
    /// <exception cref="ReceiptEntryException">得意先が存在しない、または都度得意先の場合。</exception>
    public async Task<CustomerReceivableSummary> GetReceivableSummaryAsync(
        string customerCode, CancellationToken cancellationToken = default)
    {
        await GetClosingCustomerAsync(customerCode, cancellationToken);

        var outstanding = await GetOutstandingBillingsAsync(customerCode, cancellationToken);
        if (outstanding.Count == 0)
        {
            return new CustomerReceivableSummary(0m, null, null, 0m);
        }

        var latest = outstanding.OrderByDescending(o => o.BillingDate).ThenByDescending(o => o.BillingNumber).First();
        return new CustomerReceivableSummary(
            outstanding.Sum(o => o.Outstanding), latest.BillingNumber, latest.BillingDate, latest.Outstanding);
    }

    /// <summary>
    /// 新規に入金伝票を登録する。明細行（支払手段の内訳）はそのまま<c>receipt</c>へ保存し、
    /// その合計額を確定済み請求へ古い順に配分した結果を<c>receipt_allocation</c>へ保存する。
    /// </summary>
    /// <exception cref="ReceiptEntryException">
    /// 得意先が存在しない、都度得意先、明細行が0件、明細行の合計額が0以下、振込の行で入金先口座が
    /// 未指定、または手形の行で手形期日が未指定の場合。
    /// </exception>
    public async Task<string> SaveNewAsync(
        string customerCode,
        DateOnly receiptDate,
        string? slipRemarks,
        IReadOnlyList<ReceiptLineInput> lines,
        CancellationToken cancellationToken = default)
    {
        ValidateLines(lines);
        var receiptAmount = lines.Sum(l => l.Amount);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var customer = await GetClosingCustomerAsync(customerCode, cancellationToken);
        var outstanding = await GetOutstandingBillingsAsync(customerCode, cancellationToken);
        var allocationLines = BuildAllocationLines(outstanding, receiptAmount);

        var receiptSlipNumber = await slipNumberService.NextAsync(SlipNumberKind.ReceiptSlip, cancellationToken);
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        var receiptEntities = new List<ReceiptEntity>(lines.Count);
        short lineNumber = 1;
        foreach (var line in lines)
        {
            receiptEntities.Add(new ReceiptEntity
            {
                ReceiptSlipNumber = receiptSlipNumber,
                LineNumber = lineNumber++,
                ReceiptDate = receiptDate,
                CustomerCode = customer.CustomerCode,
                TaxUnit = customer.TaxUnit,
                CustomerName = customer.CustomerName,
                ReceiptMethod = line.ReceiptMethod,
                BankAccountCode = line.BankAccountCode,
                BillDueDate = line.BillDueDate,
                Amount = line.Amount,
                AllocationStatus = AllocationStatus.Unallocated,
                SlipRemarks = slipRemarks,
                LineRemarks = line.LineRemarks,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
            });
        }

        dbContext.Receipts.AddRange(receiptEntities);

        var allocationEntities = new List<ReceiptAllocationEntity>(allocationLines.Count);
        short allocationLineNumber = 1;
        foreach (var allocation in allocationLines)
        {
            allocationEntities.Add(new ReceiptAllocationEntity
            {
                ReceiptSlipNumber = receiptSlipNumber,
                LineNumber = allocationLineNumber++,
                CustomerCode = customer.CustomerCode,
                TaxUnit = customer.TaxUnit,
                BillingNumber = allocation.BillingNumber,
                AllocatedAmount = allocation.AllocatedAmount,
                FeeAdjustmentAmount = 0m,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
            });
        }

        dbContext.ReceiptAllocations.AddRange(allocationEntities);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new ReceiptEntryException("入金の保存に失敗しました。", ex);
        }

        await settlementService.RecalculateForCustomerAsync(customer.CustomerCode, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "入金を登録しました。ReceiptSlipNumber={ReceiptSlipNumber} CustomerCode={CustomerCode} 行数={LineCount}",
            receiptSlipNumber, customer.CustomerCode, receiptEntities.Count);

        return receiptSlipNumber;
    }

    /// <summary>入金No.で1件取得する（読み取り専用表示用。保存しない）。訂正・取消はTODO.md 7-5。</summary>
    public Task<List<ReceiptEntity>> GetByNumberAsync(
        string receiptSlipNumber, CancellationToken cancellationToken = default)
        => dbContext.Receipts
            .AsNoTracking()
            .Where(r => r.ReceiptSlipNumber == receiptSlipNumber && !r.IsDeleted)
            .OrderBy(r => r.LineNumber)
            .ToListAsync(cancellationToken);

    private static void ValidateLines(IReadOnlyList<ReceiptLineInput> lines)
    {
        if (lines.Count == 0)
        {
            throw new ReceiptEntryException("明細行を1件以上入力してください。");
        }

        if (lines.Sum(l => l.Amount) <= 0m)
        {
            throw new ReceiptEntryException("入金額の合計は0より大きい値を入力してください。");
        }

        foreach (var line in lines)
        {
            switch (line.ReceiptMethod)
            {
                case ReceiptMethod.BankTransfer when string.IsNullOrWhiteSpace(line.BankAccountCode):
                    throw new ReceiptEntryException("振込の行は入金先口座を指定してください。");
                case ReceiptMethod.PromissoryNote when line.BillDueDate is null:
                    throw new ReceiptEntryException("手形の行は手形期日を指定してください。");
            }
        }
    }

    private async Task<CustomerEntity> GetClosingCustomerAsync(
        string customerCode, CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerCode == customerCode && !c.IsDeleted, cancellationToken)
            ?? throw new ReceiptEntryException($"得意先コード「{customerCode}」が見つかりません。");

        if (customer.TaxUnit == TaxUnit.Line)
        {
            throw new ReceiptEntryException(
                "都度得意先（内税明細単位）はこの画面では入金登録できません。明細入金画面をご利用ください。");
        }

        return customer;
    }

    /// <summary>
    /// 確定済み<c>billing</c>のうち未消込残額が0でないものを、古い順（<c>billing_date</c>→
    /// <c>billing_number</c>）で返す。未消込残額 = <c>current_billing_amount</c> − 既存
    /// <c>receipt_allocation</c>の充当済額（<c>allocated_amount + fee_adjustment_amount</c>）。
    /// </summary>
    private async Task<List<OutstandingBilling>> GetOutstandingBillingsAsync(
        string customerCode, CancellationToken cancellationToken)
    {
        var billings = await dbContext.Billings
            .AsNoTracking()
            .Where(b => b.CustomerCode == customerCode && !b.IsDeleted && b.BillingStatus == BillingStatus.Confirmed)
            .OrderBy(b => b.BillingDate).ThenBy(b => b.BillingNumber)
            .ToListAsync(cancellationToken);

        if (billings.Count == 0)
        {
            return [];
        }

        var billingNumbers = billings.Select(b => b.BillingNumber).ToList();
        var allocatedByBilling = await dbContext.ReceiptAllocations
            .AsNoTracking()
            .Where(a => !a.IsDeleted && a.BillingNumber != null && billingNumbers.Contains(a.BillingNumber!))
            .GroupBy(a => a.BillingNumber!)
            .Select(g => new { BillingNumber = g.Key, Total = g.Sum(a => a.AllocatedAmount + a.FeeAdjustmentAmount) })
            .ToDictionaryAsync(x => x.BillingNumber, x => x.Total, cancellationToken);

        return billings
            .Select(b => new OutstandingBilling(
                b.BillingNumber,
                b.BillingDate,
                b.CurrentBillingAmount - allocatedByBilling.GetValueOrDefault(b.BillingNumber)))
            .Where(o => o.Outstanding != 0m)
            .ToList();
    }

    /// <summary>
    /// <see cref="SettlementAllocator"/>（TODO.md 7-1）を請求単位の配分に流用する。未消込残額を
    /// 古い順に整列した対象額として渡し、入金額をその順に充当する。全額充当できない残額
    /// （前受・過入金）は<c>billing_number = NULL</c>の1行にまとめる。
    /// </summary>
    private static List<ReceiptAllocationLine> BuildAllocationLines(
        IReadOnlyList<OutstandingBilling> outstanding, decimal receiptAmount)
    {
        var targets = outstanding.Select(o => o.Outstanding).ToList();
        var allocations = SettlementAllocator.Allocate(targets, receiptAmount);

        var lines = new List<ReceiptAllocationLine>();
        for (var i = 0; i < outstanding.Count; i++)
        {
            if (allocations[i] != 0m)
            {
                lines.Add(new ReceiptAllocationLine(outstanding[i].BillingNumber, allocations[i]));
            }
        }

        var remainder = receiptAmount - allocations.Sum();
        if (remainder != 0m || lines.Count == 0)
        {
            lines.Add(new ReceiptAllocationLine(null, remainder));
        }

        return lines;
    }

    private sealed record OutstandingBilling(string BillingNumber, DateOnly BillingDate, decimal Outstanding);
}

/// <summary>入金明細行の入力（支払手段の内訳）。</summary>
public sealed record ReceiptLineInput(
    ReceiptMethod ReceiptMethod,
    string? BankAccountCode,
    DateOnly? BillDueDate,
    decimal Amount,
    string? LineRemarks);

/// <summary>
/// 入金額の配分結果（請求単位。内部データ）。<see cref="BillingNumber"/>が<c>null</c>の行は
/// 前受・過入金（充当先未定）を表す。
/// </summary>
public sealed record ReceiptAllocationLine(string? BillingNumber, decimal AllocatedAmount);

/// <summary>得意先の請求残高（画面の「請求残高」表示用）。</summary>
public sealed record CustomerReceivableSummary(
    decimal OutstandingTotal, string? LatestBillingNumber, DateOnly? LatestBillingDate, decimal LatestBillingAmount);

/// <summary>入金入力の業務ルール違反。</summary>
public sealed class ReceiptEntryException(string message, Exception? inner = null) : Exception(message, inner);
