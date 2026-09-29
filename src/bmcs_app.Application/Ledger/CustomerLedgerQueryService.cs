using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using BillingStatus = bmcs_app.Domain.Enums.BillingStatus;

namespace bmcs_app.Application.Ledger;

/// <summary>
/// 得意先元帳の照会ユースケース（TODO.md 8-1）。SQLビュー・GROUP BY は使わず、得意先の全期間の
/// 売上・入金・請求を読み出してアプリ側（Domain の純粋関数 <see cref="CustomerLedgerBuilder"/>）で
/// マージする（docs/architecture.md 7章・10章）。残高キャッシュ列は持たず都度集計する
/// （M-11・2026-09-10確定）。
/// </summary>
public class CustomerLedgerQueryService(BmcsDbContext dbContext)
{
    /// <summary>
    /// 得意先1件の元帳を組み立てる。得意先が存在しなければ null。
    /// 無効化済み（IsDeleted）の得意先も対象にする（過去の残高照会は必要なため。
    /// <c>SettlementService.RecalculateForBillingGroupAsync</c> と同じ方針）。
    /// </summary>
    public async Task<CustomerLedgerResult?> GetAsync(
        string customerCode, DateOnly periodFrom, DateOnly periodTo, CancellationToken cancellationToken = default)
    {
        var customer = await dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerCode == customerCode, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var salesLines = await dbContext.Sales.AsNoTracking()
            .Where(s => s.CustomerCode == customerCode && !s.IsDeleted)
            .OrderBy(s => s.SlipDate).ThenBy(s => s.SalesSlipNumber).ThenBy(s => s.LineNumber)
            .ToListAsync(cancellationToken);

        List<Domain.Entities.Billing> confirmedBillings = [];
        List<Domain.Entities.Receipt> receiptLines = [];
        List<Domain.Entities.DetailReceipt> detailReceiptLines = [];
        List<Domain.Entities.DetailInvoiceSalesLine> invoiceLinks = [];

        if (customer.TaxUnit == TaxUnit.Line)
        {
            detailReceiptLines = await dbContext.DetailReceipts.AsNoTracking()
                .Where(r => r.CustomerCode == customerCode && !r.IsDeleted)
                .ToListAsync(cancellationToken);

            // SettlementService.RecalculateDetailAsync と同じ引き方（invoice_status では絞らない。
            // 取消済み明細請求書に連携する行は自然に存在しなくなるため、絞り込みが不要）。
            var slipNumbers = salesLines.Select(s => s.SalesSlipNumber).Distinct().ToList();
            invoiceLinks = await dbContext.DetailInvoiceSalesLines.AsNoTracking()
                .Where(l => slipNumbers.Contains(l.SalesSlipNumber))
                .ToListAsync(cancellationToken);
        }
        else
        {
            receiptLines = await dbContext.Receipts.AsNoTracking()
                .Where(r => r.CustomerCode == customerCode && !r.IsDeleted)
                .ToListAsync(cancellationToken);

            // 解除済み（BillingStatus=Released）は集計・残高計算の対象外（product-spec.md「解除済＝集計対象外」）。
            // receipt_allocation は参照しない（締め解除がそこまで巻き戻さないため。docs/design_document.md 21章）。
            confirmedBillings = await dbContext.Billings.AsNoTracking()
                .Where(b => b.CustomerCode == customerCode && !b.IsDeleted && b.BillingStatus == BillingStatus.Confirmed)
                .ToListAsync(cancellationToken);
        }

        // 過去の入金が参照するコードの名称解決に使うため、無効化済み（IsDeleted）の入金方法も含める
        // （Customer と同じ方針。このクラスの doc comment 参照）。
        var depositMethods = await dbContext.DepositMethods.AsNoTracking().ToListAsync(cancellationToken);

        var input = new CustomerLedgerInput(
            customer, periodFrom, periodTo, salesLines, confirmedBillings, receiptLines, detailReceiptLines,
            invoiceLinks, depositMethods);

        return CustomerLedgerBuilder.Build(input);
    }

    /// <summary>
    /// 指定日時点のリアルタイム残高（TODO.md 8-2 / 9-1 から再利用する入口）。
    /// <c>GetAsync(code, asOf, asOf)</c> の <see cref="CustomerLedgerResult.ClosingBalance"/> と同値。
    /// </summary>
    public async Task<decimal?> GetBalanceAsOfAsync(
        string customerCode, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var result = await GetAsync(customerCode, asOf, asOf, cancellationToken);
        return result?.ClosingBalance;
    }
}
