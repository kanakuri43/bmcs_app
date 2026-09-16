using bmcs_app.Domain.Calculations;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>
/// 請求締め済み期間への新規登録・日付変更を防ぐ判定（申し送り事項R2の解消）のテスト。
/// </summary>
public class BillingClosedDateEvaluatorTests
{
    [Fact]
    public void 確定済み請求が無ければ制限なしで登録可能()
    {
        var result = BillingClosedDateEvaluator.Check(
            new DateOnly(2020, 1, 1), latestConfirmedBillingDate: null, "売上日付");

        Assert.True(result.IsAllowed);
        Assert.Null(result.MinimumDate);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void 締切日当日は登録できない()
    {
        var latest = new DateOnly(2026, 9, 30);

        var result = BillingClosedDateEvaluator.Check(latest, latest, "売上日付");

        Assert.False(result.IsAllowed);
        Assert.Equal(new DateOnly(2026, 10, 1), result.MinimumDate);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void 締切日より前は登録できない()
    {
        var latest = new DateOnly(2026, 9, 30);

        var result = BillingClosedDateEvaluator.Check(new DateOnly(2026, 9, 29), latest, "売上日付");

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void 締切日の翌日は登録できる()
    {
        var latest = new DateOnly(2026, 9, 30);

        var result = BillingClosedDateEvaluator.Check(new DateOnly(2026, 10, 1), latest, "売上日付");

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void 月末締めの最小日付は翌月1日になる()
    {
        var minimum = BillingClosedDateEvaluator.MinimumEntryDate(new DateOnly(2026, 9, 30));

        Assert.Equal(new DateOnly(2026, 10, 1), minimum);
    }

    [Fact]
    public void 年末締めの最小日付は翌年1月1日になる()
    {
        var minimum = BillingClosedDateEvaluator.MinimumEntryDate(new DateOnly(2026, 12, 31));

        Assert.Equal(new DateOnly(2027, 1, 1), minimum);
    }

    [Fact]
    public void 未来日付は登録できる()
    {
        var latest = new DateOnly(2026, 9, 30);

        var result = BillingClosedDateEvaluator.Check(new DateOnly(2030, 1, 1), latest, "入金日付");

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void メッセージに項目名が反映される()
    {
        var latest = new DateOnly(2026, 9, 30);

        var result = BillingClosedDateEvaluator.Check(latest, latest, "入金日付");

        Assert.Contains("入金日付", result.Reason);
    }
}
