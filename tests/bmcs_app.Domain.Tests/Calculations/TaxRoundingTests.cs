using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

public class TaxRoundingTests
{
    // 行列A: 3端数区分 × 境界値（100.0/.4/.5/.6 と符号反転）
    [Theory]
    [InlineData(100.0, RoundingType.Floor, 100)]
    [InlineData(100.4, RoundingType.Floor, 100)]
    [InlineData(100.5, RoundingType.Floor, 100)]
    [InlineData(100.6, RoundingType.Floor, 100)]
    [InlineData(-100.0, RoundingType.Floor, -100)]
    [InlineData(-100.4, RoundingType.Floor, -100)]
    [InlineData(-100.5, RoundingType.Floor, -100)]
    [InlineData(-100.6, RoundingType.Floor, -100)]
    [InlineData(100.0, RoundingType.RoundHalfUp, 100)]
    [InlineData(100.4, RoundingType.RoundHalfUp, 100)]
    [InlineData(100.5, RoundingType.RoundHalfUp, 101)]
    [InlineData(100.6, RoundingType.RoundHalfUp, 101)]
    [InlineData(-100.0, RoundingType.RoundHalfUp, -100)]
    [InlineData(-100.4, RoundingType.RoundHalfUp, -100)]
    [InlineData(-100.5, RoundingType.RoundHalfUp, -101)]
    [InlineData(-100.6, RoundingType.RoundHalfUp, -101)]
    [InlineData(100.0, RoundingType.Ceiling, 100)]
    [InlineData(100.4, RoundingType.Ceiling, 101)]
    [InlineData(100.5, RoundingType.Ceiling, 101)]
    [InlineData(100.6, RoundingType.Ceiling, 101)]
    [InlineData(-100.0, RoundingType.Ceiling, -100)]
    [InlineData(-100.4, RoundingType.Ceiling, -101)]
    [InlineData(-100.5, RoundingType.Ceiling, -101)]
    [InlineData(-100.6, RoundingType.Ceiling, -101)]
    public void 境界値を端数区分どおりに丸める(double value, RoundingType roundingType, decimal expected)
    {
        var actual = TaxRounding.RoundToYen((decimal)value, roundingType);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(RoundingType.Floor)]
    [InlineData(RoundingType.RoundHalfUp)]
    [InlineData(RoundingType.Ceiling)]
    public void ゼロは常にゼロ(RoundingType roundingType)
    {
        Assert.Equal(0m, TaxRounding.RoundToYen(0m, roundingType));
    }

    [Theory]
    [InlineData((RoundingType)0)]
    [InlineData((RoundingType)99)]
    public void 未定義の端数区分は例外(RoundingType roundingType)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TaxRounding.RoundToYen(100m, roundingType));
    }

    // 行列B: 符号対称性 — マイナスは絶対値を丸めて符号を戻す
    [Theory]
    [InlineData(RoundingType.Floor)]
    [InlineData(RoundingType.RoundHalfUp)]
    [InlineData(RoundingType.Ceiling)]
    public void 符号対称性が成り立つ(RoundingType roundingType)
    {
        decimal[] values = [0m, 0.4m, 0.5m, 0.6m, 100.4m, 100.5m, 100.6m, 12345.678m];

        foreach (var v in values)
        {
            Assert.Equal(
                -TaxRounding.RoundToYen(v, roundingType),
                TaxRounding.RoundToYen(-v, roundingType));
        }
    }
}
