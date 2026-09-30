using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 月次締め（<c>monthly_closings</c>）の得意先1行分の金額。<see cref="Breakdown"/> は税率別内訳5カラム。
/// </summary>
public sealed record MonthlyClosingAmounts(
    decimal PreviousBalance,
    decimal SalesAmount,
    decimal ReceiptAmount,
    decimal TaxAmount,
    decimal ClosingBalance,
    TaxSummary Breakdown)
{
    /// <summary>前月残高・売上・入金・当月残高がすべて0（締める意味がない）。</summary>
    public bool IsEmpty => PreviousBalance == 0m && SalesAmount == 0m && ReceiptAmount == 0m && ClosingBalance == 0m;
}

/// <summary>
/// 月次締めの金額計算（TODO.md 9-1）。当月残高は得意先元帳の月末残高をそのまま使い（完了条件
/// 「集計値が元帳の残高と一致する」）、前月残高は前月の確定行の当月残高を優先する（連続性優先）。
/// 消費税額は「当月残高 − 前月残高 − 売上 ＋ 入金」で逆算し、<see cref="TaxUnit.Invoice"/> で
/// 前月の仮計算税が請求締め後に確定値へ置き換わったずれを吸収する。詳細は
/// docs/design_document.md の月次締めの章を参照。
/// </summary>
public static class MonthlyClosingCalculator
{
    /// <param name="input">元帳の入力（期間＝暦月の月初〜月末）。</param>
    /// <param name="ledger"><paramref name="input"/> から <see cref="CustomerLedgerBuilder.Build"/> で得た結果。</param>
    /// <param name="previousClosingBalance">前月の確定行の当月残高。確定行が無ければ null（元帳の月初残高を使う）。</param>
    /// <exception cref="InvalidOperationException">
    /// <see cref="TaxUnit.Slip"/> の得意先で、再計算した伝票税額の合計が保存済み <c>slip_tax_amount</c> と一致しない場合。
    /// </exception>
    public static MonthlyClosingAmounts Calculate(
        CustomerLedgerInput input, CustomerLedgerResult ledger, decimal? previousClosingBalance)
    {
        var customer = input.Customer;
        var monthLines = input.SalesLines
            .Where(s => s.SlipDate >= input.PeriodFrom && s.SlipDate <= input.PeriodTo)
            .ToList();

        // 請求集約元: 自社の売上と税率別の対価額のみ（docs/design_document.md 21章 R8）。
        if (!customer.IsBillingRoot)
        {
            return new MonthlyClosingAmounts(
                0m, ledger.SalesTotal, 0m, 0m, 0m, TaxableBreakdown(monthLines));
        }

        decimal salesAmount;
        TaxSummary breakdown;
        if (customer.TaxUnit == TaxUnit.Line)
        {
            // 内税: sales.amount は税込。売上＝税抜額、消費税額＝内税額とし、売上＋税で税込に戻す。
            var buckets = monthLines
                .Select(s => new TaxRateBucket(
                    s.TaxCategory, s.TaxRate, s.Amount - (s.TaxAmount ?? 0m), s.TaxAmount ?? 0m))
                .ToList();
            breakdown = ConsumptionTaxCalculator.ToSummary(buckets);
            salesAmount = breakdown.TaxableAmount;
        }
        else
        {
            salesAmount = ledger.SalesTotal;
            breakdown = TaxableBreakdown(monthLines) + TaxBreakdown(input, customer, monthLines);
        }

        var previousBalance = previousClosingBalance ?? ledger.OpeningBalance;
        var taxAmount = ledger.ClosingBalance - previousBalance - salesAmount + ledger.ReceiptTotal;

        return new MonthlyClosingAmounts(
            previousBalance, salesAmount, ledger.ReceiptTotal, taxAmount, ledger.ClosingBalance, breakdown);
    }

    /// <summary>当月売上を税種別区分ごとに合計した対価額のみの内訳（税額は0）。</summary>
    private static TaxSummary TaxableBreakdown(IEnumerable<Sales> monthLines)
        => ConsumptionTaxCalculator.ToSummary(
            monthLines
                .GroupBy(s => s.TaxCategory)
                .Select(g => new TaxRateBucket(g.Key, 0m, g.Sum(s => s.Amount), 0m)));

    /// <summary>外税（請求単位・伝票単位）の税額のみの内訳（対価額は0）。</summary>
    private static TaxSummary TaxBreakdown(CustomerLedgerInput input, Customer customer, List<Sales> monthLines)
    {
        if (customer.TaxUnit == TaxUnit.Slip)
        {
            var slips = monthLines.GroupBy(s => s.SalesSlipNumber).ToList();
            var perSlip = ConsumptionTaxCalculator.CalculateExternalTaxPerSlip(
                slips.Select(g => g.Select(s => new TaxLine(s.TaxCategory, s.TaxRate, s.Amount))),
                customer.RoundingType);

            var storedTax = slips.Sum(g => g.First().SlipTaxAmount ?? 0m);
            if (storedTax != perSlip.TaxAmount)
            {
                throw new InvalidOperationException(
                    $"伝票単位の税額が保存値と一致しません。得意先={customer.CustomerCode} " +
                    $"再計算={perSlip.TaxAmount} 保存値={storedTax}");
            }

            return TaxOnly(perSlip);
        }

        // 請求単位: 当月の請求日を持つ確定済み請求の税額 ＋ 未締め区間の仮計算税の当月増分。
        var confirmed = input.ConfirmedBillings
            .Where(b => b.BillingDate >= input.PeriodFrom && b.BillingDate <= input.PeriodTo)
            .Aggregate(TaxSummary.Zero, (acc, b) => acc + new TaxSummary(
                0m, b.StandardRateTaxAmount, 0m, b.ReducedRateTaxAmount, 0m));

        var provisionalToDate = ConsumptionTaxCalculator.ToSummary(
            CustomerLedgerBuilder.ProvisionalTaxBucketsAsOf(customer, input.SalesLines, input.PeriodTo));
        var provisionalBeforeFrom = ConsumptionTaxCalculator.ToSummary(
            CustomerLedgerBuilder.ProvisionalTaxBucketsAsOf(customer, input.SalesLines, input.PeriodFrom.AddDays(-1)));

        return confirmed + TaxOnly(provisionalToDate) + Negate(TaxOnly(provisionalBeforeFrom));
    }

    private static TaxSummary TaxOnly(TaxSummary s)
        => new(0m, s.StandardRateTaxAmount, 0m, s.ReducedRateTaxAmount, 0m);

    private static TaxSummary Negate(TaxSummary s)
        => new(
            -s.StandardRateTaxableAmount, -s.StandardRateTaxAmount,
            -s.ReducedRateTaxableAmount, -s.ReducedRateTaxAmount, -s.TaxExemptAmount);
}
