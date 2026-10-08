using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using BillingStatus = bmcs_app.Domain.Enums.BillingStatus;

namespace bmcs_app.Application.Ledger;

/// <summary>
/// 得意先元帳の照会ユースケース。SQLビュー・GROUP BY は使わず、得意先の全期間の
/// 売上・入金・請求を読み出してアプリ側（Domain の純粋関数 <see cref="CustomerLedgerBuilder"/>）で
/// マージする（docs/architecture.md 7章・10章）。残高キャッシュ列は持たず都度集計する
/// 。
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
        var input = await GetInputAsync(customerCode, periodFrom, periodTo, cancellationToken);
        return input is null ? null : CustomerLedgerBuilder.Build(input);
    }

    /// <summary>
    /// <see cref="GetAsync"/> の入力（全期間の売上・入金・請求）だけを組み立てて返す。月次締め
    /// （<c>MonthlyClosingService</c>）が元帳の結果と同じ入力から税率別内訳も計算するために公開している。
    /// 得意先が存在しなければ null。
    /// </summary>
    public async Task<CustomerLedgerInput?> GetInputAsync(
        string customerCode, DateOnly periodFrom, DateOnly periodTo, CancellationToken cancellationToken = default)
    {
        var customer = await dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerCode == customerCode, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        // 請求集約先（Customer.IsBillingRoot）はグループ全体（自身＋全請求集約元）の売上・入金を
        // 含めないと残高が正しくならない（docs/design_document.md 28章）。
        // 請求集約元はグループ展開せず自身の売上のみ（＝取引履歴のみモードのスコープそのもの）。
        // 都度得意先（TaxUnit.Line）は請求集約に一切参加できない（CHECK制約）ため、
        // グループ解決クエリ自体を省略する（SettlementService.RecalculateForBillingGroupAsync
        // が TaxUnit.Line を RecalculateDetailAsync へ分岐して同クエリを回避するのと同じ理由）。
        List<string> groupCodes = [customerCode];
        if (customer is { IsBillingRoot: true, TaxUnit: not TaxUnit.Line })
        {
            groupCodes = await dbContext.Customers.AsNoTracking()
                .Where(c => c.BillingCustomerCode == customer.CustomerCode)
                .Select(c => c.CustomerCode)
                .ToListAsync(cancellationToken);
        }

        var salesLines = groupCodes.Count == 1
            ? await dbContext.Sales.AsNoTracking()
                .Where(s => s.CustomerCode == groupCodes[0] && !s.IsDeleted)
                .OrderBy(s => s.SlipDate).ThenBy(s => s.SalesSlipNumber).ThenBy(s => s.LineNumber)
                .ToListAsync(cancellationToken)
            : await dbContext.Sales.AsNoTracking()
                .Where(s => groupCodes.Contains(s.CustomerCode) && !s.IsDeleted)
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
            // receipts もグループ展開する。得意先マスタのリンク変更判定
            // （CustomerService.HasBillingChangeLockAsync）は receipts の有無を見ないため、
            // 過去に単独で入金を受けた得意先が後から請求集約元になるケースがあり得る
            // （docs/design_document.md 28-7節）。その入金は請求締め・消込ではすでに
            // グループ全体の分として扱われるため、元帳だけ単独スコープのままだと
            // 請求集約先の残高が billing.current_billing_amount と一致しなくなる。
            receiptLines = groupCodes.Count == 1
                ? await dbContext.Receipts.AsNoTracking()
                    .Where(r => r.CustomerCode == groupCodes[0] && !r.IsDeleted)
                    .ToListAsync(cancellationToken)
                : await dbContext.Receipts.AsNoTracking()
                    .Where(r => groupCodes.Contains(r.CustomerCode) && !r.IsDeleted)
                    .ToListAsync(cancellationToken);

            // billings は対象得意先自身のコードのまま（請求集約元が確定済みbillingsを自身の
            // コードで持つことはあり得ない。CustomerService.HasBillingChangeLockAsyncが
            // 確定済みbillingsを持つ得意先の請求集約元化を拒否するため）。
            // 解除済み（BillingStatus=Released）は集計・残高計算の対象外（product-spec.md「解除済＝集計対象外」）。
            // receipt_allocation は参照しない（締め解除がそこまで巻き戻さないため。docs/design_document.md 21章）。
            confirmedBillings = await dbContext.Billings.AsNoTracking()
                .Where(b => b.CustomerCode == customerCode && !b.IsDeleted && b.BillingStatus == BillingStatus.Confirmed)
                .ToListAsync(cancellationToken);
        }

        // 過去の入金が参照するコードの名称解決に使うため、無効化済み（IsDeleted）の入金方法も含める
        // （Customer と同じ方針。このクラスの doc comment 参照）。
        var depositMethods = await dbContext.DepositMethods.AsNoTracking().ToListAsync(cancellationToken);

        return new CustomerLedgerInput(
            customer, periodFrom, periodTo, salesLines, confirmedBillings, receiptLines, detailReceiptLines,
            invoiceLinks, depositMethods);
    }

    /// <summary>
    /// 指定日時点のリアルタイム残高（月次締めなど他のユースケースから再利用する入口）。
    /// <c>GetAsync(code, asOf, asOf)</c> の <see cref="CustomerLedgerResult.ClosingBalance"/> と同値。
    /// 請求集約元（取引履歴のみモード）は残高を管理しないため null を返す
    /// （残高0円と区別するため。呼び出し元は既に <c>?? 0m</c> で受けている）。
    /// </summary>
    public async Task<decimal?> GetBalanceAsOfAsync(
        string customerCode, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var result = await GetAsync(customerCode, asOf, asOf, cancellationToken);
        return result is null || result.IsTransactionHistoryOnly ? null : result.ClosingBalance;
    }
}
