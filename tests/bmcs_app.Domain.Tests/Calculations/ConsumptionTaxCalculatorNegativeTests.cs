using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>返品・値引（マイナス金額）と、その他の共通ロジック（明細金額・退化入力）のテスト。</summary>
public class ConsumptionTaxCalculatorNegativeTests
{
    // 行列J: 純粋な赤伝（返品）
    [Theory]
    [InlineData(RoundingType.Floor, -100)]
    [InlineData(RoundingType.RoundHalfUp, -101)]
    [InlineData(RoundingType.Ceiling, -101)]
    public void 純粋な返品のみの伝票(RoundingType roundingType, decimal expectedTax)
    {
        TaxLine[] lines = [new(TaxCategory.Standard, 10m, -1005m)];
        var summary = ConsumptionTaxCalculator.CalculateExternalTax(lines, roundingType);

        Assert.Equal(expectedTax, summary.StandardRateTaxAmount);
        Assert.Equal(-1005m, summary.StandardRateTaxableAmount);
    }

    // 行列J: 往復プロパティ — 売上+返品(同額マイナス)の合計は常にゼロ（決定1の核）
    [Theory]
    [InlineData(RoundingType.Floor)]
    [InlineData(RoundingType.RoundHalfUp)]
    [InlineData(RoundingType.Ceiling)]
    public void 同額の返品は税額をちょうど打ち消す(RoundingType roundingType)
    {
        decimal[] amounts = [1004m, 1005m, 1006m, 1006.25m, 1105.50m];

        foreach (var amount in amounts)
        {
            var sale = ConsumptionTaxCalculator.CalculateExternalTax(
                [new TaxLine(TaxCategory.Standard, 10m, amount)], roundingType);
            var refund = ConsumptionTaxCalculator.CalculateExternalTax(
                [new TaxLine(TaxCategory.Standard, 10m, -amount)], roundingType);

            Assert.Equal(TaxSummary.Zero, sale + refund);
        }
    }

    [Theory]
    [InlineData(RoundingType.Floor)]
    [InlineData(RoundingType.RoundHalfUp)]
    [InlineData(RoundingType.Ceiling)]
    public void 内税でも同額の返品は税額をちょうど打ち消す(RoundingType roundingType)
    {
        decimal[] amounts = [1004m, 1005m, 1006m, 1006.25m, 1105.50m];

        foreach (var amount in amounts)
        {
            var sale = ConsumptionTaxCalculator.CalculateInternalTaxBucket(
                new TaxLine(TaxCategory.Standard, 10m, amount), roundingType);
            var refund = ConsumptionTaxCalculator.CalculateInternalTaxBucket(
                new TaxLine(TaxCategory.Standard, 10m, -amount), roundingType);

            Assert.Equal(0m, sale.TaxAmount + refund.TaxAmount);
            Assert.Equal(0m, sale.TaxableAmount + refund.TaxableAmount);
        }
    }

    [Theory]
    [InlineData(RoundingType.Floor)]
    [InlineData(RoundingType.RoundHalfUp)]
    [InlineData(RoundingType.Ceiling)]
    public void 値引が同一伝票内の売上と相殺され丸め対象がゼロにならない(RoundingType roundingType)
    {
        TaxLine[] lines =
        [
            new(TaxCategory.Standard, 10m, 1005m),
            new(TaxCategory.Standard, 10m, -5m),
        ];
        var summary = ConsumptionTaxCalculator.CalculateExternalTax(lines, roundingType);

        Assert.Equal(1000m, summary.StandardRateTaxableAmount);
        Assert.Equal(100m, summary.StandardRateTaxAmount); // 1,000×10%はどの端数区分でも100
    }

    [Theory]
    [InlineData(RoundingType.Floor, -100)]
    [InlineData(RoundingType.RoundHalfUp, -101)]
    [InlineData(RoundingType.Ceiling, -101)]
    public void 純マイナスになるグループ(RoundingType roundingType, decimal expectedTax)
    {
        TaxLine[] lines =
        [
            new(TaxCategory.Standard, 10m, 1000m),
            new(TaxCategory.Standard, 10m, -2005m),
        ];
        var summary = ConsumptionTaxCalculator.CalculateExternalTax(lines, roundingType);

        Assert.Equal(-1005m, summary.StandardRateTaxableAmount);
        Assert.Equal(expectedTax, summary.StandardRateTaxAmount);
    }

    [Fact]
    public void 内税のマイナス行()
    {
        var line = new TaxLine(TaxCategory.Standard, 10m, -1000m);
        var bucket = ConsumptionTaxCalculator.CalculateInternalTaxBucket(line, RoundingType.Floor);

        Assert.Equal(-90m, bucket.TaxAmount);
        Assert.Equal(-910m, bucket.TaxableAmount);
    }

    // 行列L: 退化入力
    [Fact]
    public void 空リストは税額ゼロ()
    {
        Assert.Equal(TaxSummary.Zero, ConsumptionTaxCalculator.CalculateExternalTax([], RoundingType.Floor));
        Assert.Equal(TaxSummary.Zero, ConsumptionTaxCalculator.CalculateInternalTaxPerLine([], RoundingType.Floor));
    }

    [Fact]
    public void 金額ゼロの行は全項目ゼロ()
    {
        TaxLine[] lines = [new(TaxCategory.Standard, 10m, 0m)];
        var summary = ConsumptionTaxCalculator.CalculateExternalTax(lines, RoundingType.RoundHalfUp);

        Assert.Equal(TaxSummary.Zero, summary);
    }

    [Fact]
    public void Zeroとの加算は元の値のまま()
    {
        var summary = new TaxSummary(1000m, 100m, 500m, 40m, 200m);
        Assert.Equal(summary, TaxSummary.Zero + summary);
        Assert.Equal(summary, summary + TaxSummary.Zero);
    }

    // 行列M: CalculateLineAmount（明細金額 = 数量 × 単価。決定4）
    [Theory]
    [InlineData(3, 0.5, RoundingType.Floor, 1)]
    [InlineData(3, 0.5, RoundingType.RoundHalfUp, 2)]
    [InlineData(3, 0.5, RoundingType.Ceiling, 2)]
    [InlineData(2.5, 100, RoundingType.Floor, 250)]
    [InlineData(2.5, 100, RoundingType.RoundHalfUp, 250)]
    [InlineData(2.5, 100, RoundingType.Ceiling, 250)]
    [InlineData(-3, 0.5, RoundingType.Floor, -1)]
    [InlineData(-3, 0.5, RoundingType.RoundHalfUp, -2)]
    [InlineData(-3, 0.5, RoundingType.Ceiling, -2)]
    public void 明細金額は得意先の端数区分で1円単位に丸める(
        double quantity, double unitPrice, RoundingType roundingType, decimal expected)
    {
        var actual = ConsumptionTaxCalculator.CalculateLineAmount((decimal)quantity, (decimal)unitPrice, roundingType);
        Assert.Equal(expected, actual);
    }
}
