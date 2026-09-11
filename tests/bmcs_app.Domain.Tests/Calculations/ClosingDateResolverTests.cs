using bmcs_app.Domain.Calculations;

namespace bmcs_app.Domain.Tests.Calculations;

public class ClosingDateResolverTests
{
    [Theory]
    [InlineData(2026, 9, 20, 2026, 9, 20)] // 通常の締め日
    [InlineData(2026, 9, 99, 2026, 9, 30)] // 末日締め（9月は30日）
    [InlineData(2026, 2, 99, 2026, 2, 28)] // 末日締め（平年2月）
    [InlineData(2028, 2, 99, 2028, 2, 29)] // 末日締め（うるう年2月）
    [InlineData(2026, 2, 31, 2026, 2, 28)] // 31日締めだが2月は28日しかないため末日に丸める
    [InlineData(2028, 2, 31, 2028, 2, 29)] // 同上（うるう年）
    public void 締め日区分から実際の締め日を求める(
        int year, int month, byte closingDay, int expectedYear, int expectedMonth, int expectedDay)
    {
        var actual = ClosingDateResolver.Resolve(year, month, closingDay);
        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), actual);
    }

    [Fact]
    public void 締め日0_都度得意先は例外()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClosingDateResolver.Resolve(2026, 9, 0));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(100)]
    public void 範囲外の締め日は例外(byte closingDay)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClosingDateResolver.Resolve(2026, 9, closingDay));
    }
}
