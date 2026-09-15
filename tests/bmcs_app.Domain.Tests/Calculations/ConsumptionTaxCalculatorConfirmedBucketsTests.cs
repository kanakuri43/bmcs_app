using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>
/// <see cref="ConsumptionTaxCalculator.ResolveConfirmedBuckets"/>（TODO.md 10-5）のテスト。
/// 金額は常に確定済み<see cref="TaxSummary"/>の値をそのまま使い、税率(%)ラベルだけを
/// 明細行から拝借するという設計判断を直接検証する。
/// </summary>
public class ConsumptionTaxCalculatorConfirmedBucketsTests
{
    [Fact]
    public void 標準税率と軽減税率が混在する場合はそれぞれの明細行から税率を拝借する()
    {
        var summary = new TaxSummary(
            StandardRateTaxableAmount: 10000m, StandardRateTaxAmount: 1000m,
            ReducedRateTaxableAmount: 5000m, ReducedRateTaxAmount: 400m,
            TaxExemptAmount: 0m);
        var lines = new[]
        {
            new TaxLine(TaxCategory.Standard, 10m, 10000m),
            new TaxLine(TaxCategory.Reduced, 8m, 5000m),
        };

        var buckets = ConsumptionTaxCalculator.ResolveConfirmedBuckets(summary, lines);

        Assert.Equal(2, buckets.Count);
        Assert.Contains(buckets, b => b.TaxCategory == TaxCategory.Standard && b.TaxRate == 10m
            && b.TaxableAmount == 10000m && b.TaxAmount == 1000m);
        Assert.Contains(buckets, b => b.TaxCategory == TaxCategory.Reduced && b.TaxRate == 8m
            && b.TaxableAmount == 5000m && b.TaxAmount == 400m);
    }

    [Fact]
    public void 対価額と税額がともに0の区分は出力しない()
    {
        var summary = new TaxSummary(
            StandardRateTaxableAmount: 10000m, StandardRateTaxAmount: 1000m,
            ReducedRateTaxableAmount: 0m, ReducedRateTaxAmount: 0m,
            TaxExemptAmount: 0m);
        var lines = new[] { new TaxLine(TaxCategory.Standard, 10m, 10000m) };

        var buckets = ConsumptionTaxCalculator.ResolveConfirmedBuckets(summary, lines);

        Assert.Single(buckets);
        Assert.Equal(TaxCategory.Standard, buckets[0].TaxCategory);
    }

    [Fact]
    public void 非課税は税率0で出力し明細行の有無に関わらず対価額があれば表示する()
    {
        var summary = new TaxSummary(
            StandardRateTaxableAmount: 0m, StandardRateTaxAmount: 0m,
            ReducedRateTaxableAmount: 0m, ReducedRateTaxAmount: 0m,
            TaxExemptAmount: 3000m);
        var lines = new[] { new TaxLine(TaxCategory.TaxExempt, 0m, 3000m) };

        var buckets = ConsumptionTaxCalculator.ResolveConfirmedBuckets(summary, lines);

        Assert.Single(buckets);
        Assert.Equal(TaxCategory.TaxExempt, buckets[0].TaxCategory);
        Assert.Equal(0m, buckets[0].TaxRate);
        Assert.Equal(3000m, buckets[0].TaxableAmount);
    }

    [Fact]
    public void 明細行に該当区分が無い場合は税率0でフォールバックする()
    {
        // 想定外だが、締め解除・取消後に明細0件で印刷される既存の許容仕様（12-1節）と同じ理由で
        // 例外にはせず税率0で埋める（金額はヘッダーの確定値のみで印字が成立するようにする）。
        var summary = new TaxSummary(
            StandardRateTaxableAmount: 10000m, StandardRateTaxAmount: 1000m,
            ReducedRateTaxableAmount: 0m, ReducedRateTaxAmount: 0m,
            TaxExemptAmount: 0m);

        var buckets = ConsumptionTaxCalculator.ResolveConfirmedBuckets(summary, []);

        Assert.Single(buckets);
        Assert.Equal(0m, buckets[0].TaxRate);
        Assert.Equal(10000m, buckets[0].TaxableAmount);
    }
}
