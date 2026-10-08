using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>
/// 税区分3種×端数区分3種、全組み合わせの単体テストのうち
/// 主対象となる9セルの行列。得意先の税区分（TaxUnit.Invoice/Slip/Line）ごとに
/// 手計算した期待値を明示する。
/// </summary>
public class TaxUnitCombinationTests
{
    // 外税 fixture（Invoice / Slip 共通）:
    //   伝票A: A1 標準¥1,005 / A2 軽減¥1,006.25
    //   伝票B: B1 標準¥2,003 / B2 標準-¥1,005（返品）
    private static readonly TaxLine[] SlipA =
    [
        new(TaxCategory.Standard, 10m, 1005m),
        new(TaxCategory.Reduced, 8m, 1006.25m),
    ];

    private static readonly TaxLine[] SlipB =
    [
        new(TaxCategory.Standard, 10m, 2003m),
        new(TaxCategory.Standard, 10m, -1005m),
    ];

    // TaxUnit.Invoice: 請求全体（=2伝票分をまとめて）で税率ごとに1回だけ端数処理する。
    // 標準: 2,003(=B1+B2の純額) → raw 200.3、軽減: 1,006.25 → raw 80.5
    [Theory]
    [InlineData(RoundingType.Floor, 200, 80, 280)]
    [InlineData(RoundingType.RoundHalfUp, 200, 81, 281)]
    [InlineData(RoundingType.Ceiling, 201, 81, 282)]
    public void 請求単位_請求全体で税率ごとに1回端数処理する(
        RoundingType roundingType, decimal expectedStandardTax, decimal expectedReducedTax, decimal expectedTotalTax)
    {
        var summary = ConsumptionTaxCalculator.CalculateExternalTax([.. SlipA, .. SlipB], roundingType);

        Assert.Equal(2003m, summary.StandardRateTaxableAmount);
        Assert.Equal(expectedStandardTax, summary.StandardRateTaxAmount);
        Assert.Equal(1006.25m, summary.ReducedRateTaxableAmount);
        Assert.Equal(expectedReducedTax, summary.ReducedRateTaxAmount);
        Assert.Equal(expectedTotalTax, summary.TaxAmount);
    }

    // TaxUnit.Slip: 伝票ごとに税率ごとに1回端数処理してから合算する。
    // 伝票A 標準: 1,005 → raw 100.5 / 軽減: 1,006.25 → raw 80.5
    // 伝票B 標準: 998(=2,003-1,005) → raw 99.8
    [Theory]
    [InlineData(RoundingType.Floor, 199, 80, 279)]
    [InlineData(RoundingType.RoundHalfUp, 201, 81, 282)]
    [InlineData(RoundingType.Ceiling, 201, 81, 282)]
    public void 伝票単位_伝票ごとに税率ごと1回端数処理してから合算する(
        RoundingType roundingType, decimal expectedStandardTax, decimal expectedReducedTax, decimal expectedTotalTax)
    {
        var summary = ConsumptionTaxCalculator.CalculateExternalTaxPerSlip([SlipA, SlipB], roundingType);

        Assert.Equal(expectedStandardTax, summary.StandardRateTaxAmount);
        Assert.Equal(expectedReducedTax, summary.ReducedRateTaxAmount);
        Assert.Equal(expectedTotalTax, summary.TaxAmount);
    }

    // Invoice と Slip で答えが変わることを明示する（切捨: 280 ≠ 279）。
    [Fact]
    public void 請求単位と伝票単位は同じ伝票でも税額が異なりうる_切捨()
    {
        var invoice = ConsumptionTaxCalculator.CalculateExternalTax([.. SlipA, .. SlipB], RoundingType.Floor);
        var slip = ConsumptionTaxCalculator.CalculateExternalTaxPerSlip([SlipA, SlipB], RoundingType.Floor);

        Assert.Equal(280m, invoice.TaxAmount);
        Assert.Equal(279m, slip.TaxAmount);
        Assert.NotEqual(invoice.TaxAmount, slip.TaxAmount);
    }

    // TaxUnit.Line（内税明細単位）fixture。Amount は税込。
    //   L1 標準¥1,105.50 / L2 軽減¥1,000 / L3 標準-¥1,105.50（返品） / L4 非課税¥500 / L5 標準¥1,000
    private static readonly TaxLine[] LineFixture =
    [
        new(TaxCategory.Standard, 10m, 1105.50m),
        new(TaxCategory.Reduced, 8m, 1000m),
        new(TaxCategory.Standard, 10m, -1105.50m),
        new(TaxCategory.TaxExempt, 0m, 500m),
        new(TaxCategory.Standard, 10m, 1000m),
    ];

    // L1: raw100.5, L3: -raw100.5(相殺で0), L5: raw90.909...、L2: raw74.074...
    // Ceiling は L2(74.074→75) が Floor/RoundHalfUp(74) と異なる点に注意。
    [Theory]
    [InlineData(RoundingType.Floor, 90, 74, 164)]
    [InlineData(RoundingType.RoundHalfUp, 91, 74, 165)]
    [InlineData(RoundingType.Ceiling, 91, 75, 166)]
    public void 内税明細単位_明細行ごとに端数処理する(
        RoundingType roundingType, decimal expectedStandardTax, decimal expectedReducedTax, decimal expectedTotalTax)
    {
        var summary = ConsumptionTaxCalculator.CalculateInternalTaxPerLine(LineFixture, roundingType);

        Assert.Equal(expectedStandardTax, summary.StandardRateTaxAmount);
        Assert.Equal(expectedReducedTax, summary.ReducedRateTaxAmount);
        Assert.Equal(500m, summary.TaxExemptAmount);
        Assert.Equal(expectedTotalTax, summary.TaxAmount);

        // 自己検証: 税込合計は常に一致する（税抜対価額＋税額＝税込合計）。
        var expectedTotalAmount = LineFixture.Sum(l => l.Amount);
        Assert.Equal(expectedTotalAmount, summary.TotalAmount);
    }
}
