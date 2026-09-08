using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>外税（TaxUnit.Invoice / TaxUnit.Slip）計算のテスト。</summary>
public class ConsumptionTaxCalculatorExternalTests
{
    // 行列C: 外税・単一税率 × 3端数区分
    [Theory]
    [InlineData(1000, 10, RoundingType.Floor, 100)]
    [InlineData(1000, 10, RoundingType.RoundHalfUp, 100)]
    [InlineData(1000, 10, RoundingType.Ceiling, 100)]
    [InlineData(1004, 10, RoundingType.Floor, 100)]
    [InlineData(1004, 10, RoundingType.RoundHalfUp, 100)]
    [InlineData(1004, 10, RoundingType.Ceiling, 101)]
    [InlineData(1005, 10, RoundingType.Floor, 100)]
    [InlineData(1005, 10, RoundingType.RoundHalfUp, 101)]
    [InlineData(1005, 10, RoundingType.Ceiling, 101)]
    [InlineData(1006, 10, RoundingType.Floor, 100)]
    [InlineData(1006, 10, RoundingType.RoundHalfUp, 101)]
    [InlineData(1006, 10, RoundingType.Ceiling, 101)]
    [InlineData(1000, 8, RoundingType.Floor, 80)]
    [InlineData(1000, 8, RoundingType.RoundHalfUp, 80)]
    [InlineData(1000, 8, RoundingType.Ceiling, 80)]
    [InlineData(1006, 8, RoundingType.Floor, 80)]
    [InlineData(1006, 8, RoundingType.RoundHalfUp, 80)]
    [InlineData(1006, 8, RoundingType.Ceiling, 81)]
    [InlineData(1006.25, 8, RoundingType.Floor, 80)]
    [InlineData(1006.25, 8, RoundingType.RoundHalfUp, 81)]
    [InlineData(1006.25, 8, RoundingType.Ceiling, 81)]
    [InlineData(1062, 8, RoundingType.Floor, 84)]
    [InlineData(1062, 8, RoundingType.RoundHalfUp, 85)]
    [InlineData(1062, 8, RoundingType.Ceiling, 85)]
    public void 標準税率の単一グループ(double amount, double rate, RoundingType roundingType, decimal expectedTax)
    {
        var lines = new[] { new TaxLine(TaxCategory.Standard, (decimal)rate, (decimal)amount) };
        var summary = ConsumptionTaxCalculator.CalculateExternalTax(lines, roundingType);
        Assert.Equal(expectedTax, summary.StandardRateTaxAmount);
        Assert.Equal((decimal)amount, summary.StandardRateTaxableAmount);
    }

    // 行列D: 端数処理は税率ごとに1回。行ごとに積み上げてはいけない
    [Theory]
    [InlineData(RoundingType.Floor, 301)]
    [InlineData(RoundingType.RoundHalfUp, 302)]
    [InlineData(RoundingType.Ceiling, 302)]
    public void 税率ごとに1回だけ端数処理する(RoundingType roundingType, decimal expectedTax)
    {
        TaxLine[] lines =
        [
            new(TaxCategory.Standard, 10m, 1005m),
            new(TaxCategory.Standard, 10m, 1005m),
            new(TaxCategory.Standard, 10m, 1005m),
        ];

        var summary = ConsumptionTaxCalculator.CalculateExternalTax(lines, roundingType);

        Assert.Equal(expectedTax, summary.StandardRateTaxAmount);
        Assert.Equal(3015m, summary.StandardRateTaxableAmount);
    }

    // 行列E: 混在税率は税率ごとに独立して丸める
    [Theory]
    [InlineData(RoundingType.Floor, 100, 80, 180)]
    [InlineData(RoundingType.RoundHalfUp, 101, 81, 182)]
    [InlineData(RoundingType.Ceiling, 101, 81, 182)]
    public void 標準税率と軽減税率は独立に丸める(
        RoundingType roundingType, decimal expectedStandardTax, decimal expectedReducedTax, decimal expectedTotalTax)
    {
        TaxLine[] lines =
        [
            new(TaxCategory.Standard, 10m, 1005m),
            new(TaxCategory.Reduced, 8m, 1006.25m),
        ];

        var summary = ConsumptionTaxCalculator.CalculateExternalTax(lines, roundingType);

        Assert.Equal(expectedStandardTax, summary.StandardRateTaxAmount);
        Assert.Equal(1005m, summary.StandardRateTaxableAmount);
        Assert.Equal(expectedReducedTax, summary.ReducedRateTaxAmount);
        Assert.Equal(1006.25m, summary.ReducedRateTaxableAmount);
        Assert.Equal(expectedTotalTax, summary.TaxAmount);
    }

    // 行列F: 非課税
    [Fact]
    public void 非課税単独は税額ゼロ()
    {
        TaxLine[] lines = [new(TaxCategory.TaxExempt, 0m, 500m)];
        var summary = ConsumptionTaxCalculator.CalculateExternalTax(lines, RoundingType.RoundHalfUp);

        Assert.Equal(500m, summary.TaxExemptAmount);
        Assert.Equal(0m, summary.TaxAmount);
        Assert.Equal(0m, summary.StandardRateTaxableAmount);
        Assert.Equal(0m, summary.ReducedRateTaxableAmount);
    }

    [Fact]
    public void 非課税は標準税率の対価額に混入しない()
    {
        TaxLine[] lines =
        [
            new(TaxCategory.TaxExempt, 0m, 500m),
            new(TaxCategory.Standard, 10m, 1005m),
        ];
        var summary = ConsumptionTaxCalculator.CalculateExternalTax(lines, RoundingType.Floor);

        Assert.Equal(500m, summary.TaxExemptAmount);
        Assert.Equal(1005m, summary.StandardRateTaxableAmount);
        Assert.Equal(100m, summary.StandardRateTaxAmount);
    }

    [Fact]
    public void 非課税は不正な税率が入っていても税額ゼロ()
    {
        // 非課税行にゴミの税率が入っていても、税率を参照せず税額0にする
        // （database-schema.md 2.3節「非課税は本マスタを参照しない」）。
        TaxLine[] lines = [new(TaxCategory.TaxExempt, 10m, 500m)];
        var summary = ConsumptionTaxCalculator.CalculateExternalTax(lines, RoundingType.RoundHalfUp);

        Assert.Equal(0m, summary.TaxAmount);
        Assert.Equal(500m, summary.TaxExemptAmount);
    }

    [Fact]
    public void 非課税のマイナス値()
    {
        TaxLine[] lines = [new(TaxCategory.TaxExempt, 0m, -500m)];
        var summary = ConsumptionTaxCalculator.CalculateExternalTax(lines, RoundingType.Floor);

        Assert.Equal(-500m, summary.TaxExemptAmount);
        Assert.Equal(0m, summary.TaxAmount);
    }

    [Theory]
    [InlineData(RoundingType.Floor)]
    [InlineData(RoundingType.RoundHalfUp)]
    [InlineData(RoundingType.Ceiling)]
    public void 全行非課税なら税額ゼロ(RoundingType roundingType)
    {
        TaxLine[] lines =
        [
            new(TaxCategory.TaxExempt, 0m, 300m),
            new(TaxCategory.TaxExempt, 0m, 700m),
        ];
        var summary = ConsumptionTaxCalculator.CalculateExternalTax(lines, roundingType);

        Assert.Equal(0m, summary.TaxAmount);
        Assert.Equal(1000m, summary.TaxExemptAmount);
    }

    // 行列G: 標準税率内で2つの税率（税率改定期間をまたぐ請求）。
    // @10%: raw100.5 / @8%: raw80.4 と端数の出方が違うため、丸め結果は端数区分ごとに変わる。
    [Theory]
    [InlineData(RoundingType.Floor, 180)]
    [InlineData(RoundingType.RoundHalfUp, 181)]
    [InlineData(RoundingType.Ceiling, 182)]
    public void 同じ税種別区分内の異なる税率は別々に丸めて合算する(RoundingType roundingType, decimal expectedTax)
    {
        TaxLine[] lines =
        [
            new(TaxCategory.Standard, 10m, 1005m),
            new(TaxCategory.Standard, 8m, 1005m),
        ];

        var buckets = ConsumptionTaxCalculator.CalculateExternalTaxBuckets(lines, roundingType);
        Assert.Equal(2, buckets.Count);

        var summary = ConsumptionTaxCalculator.ToSummary(buckets);
        Assert.Equal(2010m, summary.StandardRateTaxableAmount);
        Assert.Equal(expectedTax, summary.StandardRateTaxAmount);
    }

    // 行列I: 暫定C-4b — 伝票単位は伝票ごとに端数処理してから積み上げる（請求全体で1回の丸め直しではない）
    [Theory]
    [InlineData(RoundingType.Floor, 200, 201)]
    [InlineData(RoundingType.RoundHalfUp, 202, 201)]
    [InlineData(RoundingType.Ceiling, 202, 201)]
    public void 伝票単位は伝票ごとに端数処理する_暫定C4b(
        RoundingType roundingType, decimal expectedPerSlipTax, decimal expectedCombinedTax)
    {
        TaxLine[] slipA = [new(TaxCategory.Standard, 10m, 1005m)];
        TaxLine[] slipB = [new(TaxCategory.Standard, 10m, 1005m)];

        var perSlip = ConsumptionTaxCalculator.CalculateExternalTaxPerSlip([slipA, slipB], roundingType);
        Assert.Equal(expectedPerSlipTax, perSlip.StandardRateTaxAmount);

        // 対比: 2伝票分を1グループとしてまとめて丸め直すと異なる値になる（採用しない方式）。
        var combined = ConsumptionTaxCalculator.CalculateExternalTax([.. slipA, .. slipB], roundingType);
        Assert.Equal(expectedCombinedTax, combined.StandardRateTaxAmount);
    }
}
