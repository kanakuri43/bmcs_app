using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

public class TaxRateResolverTests
{
    // わざと未ソートで渡す。並び順に依存しないことを確認する。
    private static readonly IReadOnlyList<TaxRateMaster> Masters =
    [
        NewMaster(new DateOnly(2019, 10, 1), 10.00m, 8.00m),
        NewMaster(new DateOnly(2027, 4, 1), 12.00m, 8.00m),
        NewMaster(new DateOnly(2014, 4, 1), 8.00m, 8.00m),
    ];

    [Theory]
    [InlineData(2019, 10, 1, 10.00, 8.00)]   // 施行日ちょうど（境界、inclusive）
    [InlineData(2019, 9, 30, 8.00, 8.00)]    // 1日前 → 直前のレコード
    [InlineData(2020, 6, 15, 10.00, 8.00)]   // 中間
    [InlineData(2030, 1, 1, 12.00, 8.00)]    // 最新レコードより後
    [InlineData(2026, 9, 8, 10.00, 8.00)]    // 今日。未来のレコード(2027-04-01)を採用してはいけない
    public void 伝票日付以前で最も新しいレコードを適用する(
        int year, int month, int day, double expectedStandard, double expectedReduced)
    {
        var slipDate = new DateOnly(year, month, day);

        Assert.Equal((decimal)expectedStandard, TaxRateResolver.ResolveRate(Masters, slipDate, TaxCategory.Standard));
        Assert.Equal((decimal)expectedReduced, TaxRateResolver.ResolveRate(Masters, slipDate, TaxCategory.Reduced));
    }

    [Fact]
    public void 最古のレコードより前は例外()
    {
        var slipDate = new DateOnly(2014, 3, 31);

        Assert.Throws<InvalidOperationException>(
            () => TaxRateResolver.ResolveRate(Masters, slipDate, TaxCategory.Standard));
        Assert.Throws<InvalidOperationException>(
            () => TaxRateResolver.ResolveRate(Masters, slipDate, TaxCategory.Reduced));
    }

    [Fact]
    public void 非課税はどの日付でも税率ゼロでマスタを参照しない()
    {
        var slipDate = new DateOnly(2014, 3, 31); // マスタが無くても例外にならない
        Assert.Equal(0m, TaxRateResolver.ResolveRate(Masters, slipDate, TaxCategory.TaxExempt));
    }

    [Fact]
    public void 非課税は空リストでも税率ゼロ()
    {
        Assert.Equal(0m, TaxRateResolver.ResolveRate([], new DateOnly(2026, 9, 8), TaxCategory.TaxExempt));
    }

    [Fact]
    public void 未対応の税種別区分は例外()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TaxRateResolver.ResolveRate(Masters, new DateOnly(2026, 9, 8), (TaxCategory)99));
    }

    [Fact]
    public void FindApplicableは該当なしでnull()
    {
        Assert.Null(TaxRateResolver.FindApplicable(Masters, new DateOnly(2014, 3, 31)));
    }

    private static TaxRateMaster NewMaster(DateOnly effectiveDate, decimal standardRate, decimal reducedRate)
        => new()
        {
            EffectiveDate = effectiveDate,
            StandardTaxRate = standardRate,
            ReducedTaxRate = reducedRate,
            CreatedBy = "TEST",
            CreatedAt = DateTime.Now,
            UpdatedBy = "TEST",
            UpdatedAt = DateTime.Now,
        };
}
