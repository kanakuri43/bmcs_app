using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using ReceiptEntity = bmcs_app.Domain.Entities.Receipt;

namespace bmcs_app.Application.Receipt;

/// <summary>
/// 入金入力画面（TODO.md 7-2）のユースケース。締め得意先（<see cref="TaxUnit.Invoice"/>／
/// <see cref="TaxUnit.Slip"/>）専用。都度得意先（<see cref="TaxUnit.Line"/>）の入金は
/// 明細入金画面（TODO.md 7-4）が担う。
///
/// 締め得意先は請求単位で古い順に自動消込する（docs/product-spec.md 共通業務ルール4）。
/// 入金額を、確定済み<c>billing</c>の未消込残額（<c>current_billing_amount</c> − 既存<c>receipt</c>
/// の充当済額）へ古い順（<c>billing_date</c>→<c>billing_number</c>）に配分し、請求1件につき
/// <c>receipt</c>の行を1件作る。配分そのものは新規のDomainクラスを作らず、7-1で実装済みの
/// <see cref="SettlementAllocator"/>（「入金額をpool、対象額の並びをtargetsとして渡し古い順に
/// 割り当てる」汎用関数）をそのまま流用する。全額を割り当てきれない残額（前受・過入金）は
/// <c>billing_number = NULL</c>の1行にまとめる（<c>receipt.billing_number</c>の意味そのもの。
/// docs/database-schema.md 2.10節）。
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
    /// 入金額を確定済み請求へ古い順に配分した結果を返す（保存しない）。画面が得意先・入金額の
    /// 入力に応じて明細行を自動生成するために使う。
    /// </summary>
    /// <exception cref="ReceiptEntryException">得意先が存在しない、または都度得意先の場合。</exception>
    public async Task<IReadOnlyList<ReceiptAllocationLine>> PreviewAllocationAsync(
        string customerCode, decimal receiptAmount, CancellationToken cancellationToken = default)
    {
        await GetClosingCustomerAsync(customerCode, cancellationToken);

        var outstanding = await GetOutstandingBillingsAsync(customerCode, cancellationToken);
        return BuildAllocationLines(outstanding, receiptAmount);
    }

    /// <summary>
    /// 新規に入金伝票を登録する。<see cref="PreviewAllocationAsync"/>と同じ配分ロジックを
    /// トランザクション内で再計算してから保存する（プレビューと保存の間に他ユーザーが
    /// 請求・入金を登録していても、古いプレビュー結果ではなくその時点の実データで配分するため。
    /// <see cref="Billing.BillingClosingService.ConfirmAsync"/>と同じ「確定は同条件で再取得した
    /// 結果を使う」方針）。
    /// </summary>
    /// <exception cref="ReceiptEntryException">
    /// 得意先が存在しない、都度得意先、振込で入金先口座が未指定、または入金額が0以下の場合。
    /// </exception>
    public async Task<string> SaveNewAsync(
        string customerCode,
        DateOnly receiptDate,
        ReceiptMethod receiptMethod,
        string? bankAccountCode,
        decimal receiptAmount,
        string? slipRemarks,
        CancellationToken cancellationToken = default)
    {
        if (receiptAmount <= 0m)
        {
            throw new ReceiptEntryException("入金額は0より大きい値を入力してください。");
        }

        if (receiptMethod == ReceiptMethod.BankTransfer && string.IsNullOrWhiteSpace(bankAccountCode))
        {
            throw new ReceiptEntryException("振込の場合は入金先口座を指定してください。");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var customer = await GetClosingCustomerAsync(customerCode, cancellationToken);
        var outstanding = await GetOutstandingBillingsAsync(customerCode, cancellationToken);
        var allocationLines = BuildAllocationLines(outstanding, receiptAmount);

        var receiptSlipNumber = await slipNumberService.NextAsync(SlipNumberKind.ReceiptSlip, cancellationToken);
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        var entities = new List<ReceiptEntity>(allocationLines.Count);
        short lineNumber = 1;
        foreach (var line in allocationLines)
        {
            entities.Add(new ReceiptEntity
            {
                ReceiptSlipNumber = receiptSlipNumber,
                LineNumber = lineNumber++,
                ReceiptDate = receiptDate,
                CustomerCode = customer.CustomerCode,
                TaxUnit = customer.TaxUnit,
                CustomerName = customer.CustomerName,
                ReceiptMethod = receiptMethod,
                BankAccountCode = receiptMethod == ReceiptMethod.BankTransfer ? bankAccountCode : null,
                ReceiptAmount = receiptAmount,
                BillingNumber = line.BillingNumber,
                AllocatedAmount = line.AllocatedAmount,
                FeeAdjustmentAmount = 0m,
                AllocationStatus = AllocationStatus.Unallocated,
                SlipRemarks = slipRemarks,
                LineRemarks = null,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
            });
        }

        dbContext.Receipts.AddRange(entities);

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
            receiptSlipNumber, customer.CustomerCode, entities.Count);

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
    /// <c>receipt</c>の充当済額（<c>allocated_amount + fee_adjustment_amount</c>）。
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
        var allocatedByBilling = await dbContext.Receipts
            .AsNoTracking()
            .Where(r => !r.IsDeleted && r.BillingNumber != null && billingNumbers.Contains(r.BillingNumber!))
            .GroupBy(r => r.BillingNumber!)
            .Select(g => new { BillingNumber = g.Key, Total = g.Sum(r => r.AllocatedAmount + r.FeeAdjustmentAmount) })
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
                lines.Add(new ReceiptAllocationLine(
                    outstanding[i].BillingNumber, outstanding[i].BillingDate, outstanding[i].Outstanding, allocations[i]));
            }
        }

        var remainder = receiptAmount - allocations.Sum();
        if (remainder != 0m || lines.Count == 0)
        {
            lines.Add(new ReceiptAllocationLine(null, null, null, remainder));
        }

        return lines;
    }

    private sealed record OutstandingBilling(string BillingNumber, DateOnly BillingDate, decimal Outstanding);
}

/// <summary>
/// 入金額の配分結果（請求単位）。<see cref="BillingNumber"/>が<c>null</c>の行は前受・過入金
/// （充当先未定）を表す。
/// </summary>
public sealed record ReceiptAllocationLine(
    string? BillingNumber, DateOnly? BillingDate, decimal? Outstanding, decimal AllocatedAmount);

/// <summary>入金入力の業務ルール違反。</summary>
public sealed class ReceiptEntryException(string message, Exception? inner = null) : Exception(message, inner);
