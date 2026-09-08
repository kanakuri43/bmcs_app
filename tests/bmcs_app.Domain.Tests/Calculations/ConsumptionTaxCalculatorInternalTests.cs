using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>内税（TaxUnit.Line、内税明細単位）計算のテスト。Amount は税込金額。</summary>
public class ConsumptionTaxCalculatorInternalTests
{
    // 行列H: 内税・明細行ごと × 3端数区分
    [Theory]
    [InlineData(1100, 10, RoundingType.Floor, 100, 1000)]
    [InlineData(1100, 10, RoundingType.RoundHalfUp, 100, 1000)]
    [InlineData(1100, 10, RoundingType.Ceiling, 100, 1000)]
    [InlineData(1000, 10, RoundingType.Floor, 90, 910)]
    [InlineData(1000, 10, RoundingType.RoundHalfUp, 91, 909)]
    [InlineData(1000, 10, RoundingType.Ceiling, 91, 909)]
    [InlineData(1105.50, 10, RoundingType.Floor, 100, 1005.50)]
    [InlineData(1105.50, 10, RoundingType.RoundHalfUp, 101, 1004.50)]
    [InlineData(1105.50, 10, RoundingType.Ceiling, 101, 1004.50)]
    [InlineData(1080, 8, RoundingType.Floor, 80, 1000)]
    [InlineData(1080, 8, RoundingType.RoundHalfUp, 80, 1000)]
    [InlineData(1080, 8, RoundingType.Ceiling, 80, 1000)]
    [InlineData(1000, 8, RoundingType.Floor, 74, 926)]
    [InlineData(1000, 8, RoundingType.RoundHalfUp, 74, 926)]
    [InlineData(1000, 8, RoundingType.Ceiling, 75, 925)]
    public void 内税明細1行の税額と税抜対価額(
        double amount, double rate, RoundingType roundingType, decimal expectedTax, decimal expectedTaxable)
    {
        var category = rate == 10 ? TaxCategory.Standard : TaxCategory.Reduced;
        var line = new TaxLine(category, (decimal)rate, (decimal)amount);

        Assert.Equal(expectedTax, ConsumptionTaxCalculator.CalculateInternalTaxAmount(line, roundingType));

        var bucket = ConsumptionTaxCalculator.CalculateInternalTaxBucket(line, roundingType);
        Assert.Equal(expectedTax, bucket.TaxAmount);
        Assert.Equal(expectedTaxable, bucket.TaxableAmount);
    }

    [Theory]
    [InlineData(RoundingType.Floor)]
    [InlineData(RoundingType.RoundHalfUp)]
    [InlineData(RoundingType.Ceiling)]
    public void 内税の非課税は税額ゼロで税込額全額が対価額(RoundingType roundingType)
    {
        var line = new TaxLine(TaxCategory.TaxExempt, 0m, 500m);

        Assert.Equal(0m, ConsumptionTaxCalculator.CalculateInternalTaxAmount(line, roundingType));

        var bucket = ConsumptionTaxCalculator.CalculateInternalTaxBucket(line, roundingType);
        Assert.Equal(500m, bucket.TaxableAmount);
    }

    // 行列I: 暫定C-4b — 内税明細単位は明細行ごとに端数処理する（一括の再計算ではない）
    [Theory]
    [InlineData(RoundingType.Floor, 270)]
    [InlineData(RoundingType.RoundHalfUp, 273)]
    [InlineData(RoundingType.Ceiling, 273)]
    public void 内税明細単位は明細行ごとに端数処理する_暫定C4b(RoundingType roundingType, decimal expectedTax)
    {
        TaxLine[] lines =
        [
            new(TaxCategory.Standard, 10m, 1000m),
            new(TaxCategory.Standard, 10m, 1000m),
            new(TaxCategory.Standard, 10m, 1000m),
        ];

        var summary = ConsumptionTaxCalculator.CalculateInternalTaxPerLine(lines, roundingType);
        Assert.Equal(expectedTax, summary.StandardRateTaxAmount);
    }

    [Fact]
    public void 明細行ごとの積み上げは一括で丸め直した値と異なる_切捨()
    {
        // 一括（3行合計3,000円をまとめて丸める）なら 272円になるが、
        // 明細行ごとの端数処理（暫定C-4b）では 270円になる。両者が異なることを明示する。
        TaxLine[] lines =
        [
            new(TaxCategory.Standard, 10m, 1000m),
            new(TaxCategory.Standard, 10m, 1000m),
            new(TaxCategory.Standard, 10m, 1000m),
        ];

        var perLine = ConsumptionTaxCalculator.CalculateInternalTaxPerLine(lines, RoundingType.Floor);
        var combined = ConsumptionTaxCalculator.CalculateInternalTaxAmount(
            new TaxLine(TaxCategory.Standard, 10m, 3000m), RoundingType.Floor);

        Assert.Equal(270m, perLine.StandardRateTaxAmount);
        Assert.Equal(272m, combined);
        Assert.NotEqual(combined, perLine.StandardRateTaxAmount);
    }
}
