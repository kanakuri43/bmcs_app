using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>返品・値引（TODO.md 5-4）の数量符号・原価正規化のテスト。</summary>
public class SalesSlipTypeRulesTests
{
    [Theory]
    [InlineData(3, 3)]
    [InlineData(-3, 3)]
    [InlineData(0, 0)]
    public void 売上は数量を正に正規化する(decimal input, decimal expected)
    {
        Assert.Equal(expected, SalesSlipTypeRules.NormalizeQuantity(SlipType.Sales, input));
    }

    [Theory]
    [InlineData(SlipType.Return)]
    [InlineData(SlipType.Discount)]
    public void 返品と値引は数量を負に正規化する(SlipType slipType)
    {
        Assert.Equal(-3m, SalesSlipTypeRules.NormalizeQuantity(slipType, 3m));
        Assert.Equal(-3m, SalesSlipTypeRules.NormalizeQuantity(slipType, -3m));
        Assert.Equal(0m, SalesSlipTypeRules.NormalizeQuantity(slipType, 0m));
    }

    [Fact]
    public void 売上と返品は商品原価をそのまま使う()
    {
        Assert.Equal(700m, SalesSlipTypeRules.NormalizeCostPrice(SlipType.Sales, 700m));
        Assert.Equal(700m, SalesSlipTypeRules.NormalizeCostPrice(SlipType.Return, 700m));
    }

    [Fact]
    public void 値引の原価は常に0()
    {
        Assert.Equal(0m, SalesSlipTypeRules.NormalizeCostPrice(SlipType.Discount, 700m));
        Assert.Equal(0m, SalesSlipTypeRules.NormalizeCostPrice(SlipType.Discount, 0m));
    }
}
