namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 得意先マスタの締め日区分（<c>customer.closing_day</c>）から、指定した対象年月の
/// 実際の締め日を求める。純粋関数のみで、DBアクセスは行わない。
/// </summary>
public static class ClosingDateResolver
{
    /// <summary>
    /// <paramref name="closingDay"/>: <c>1</c>〜<c>31</c>＝締め日（実日付）、<c>99</c>＝末日締め。
    /// <c>0</c>（都度・明細）は締め対象外のため呼び出し禁止。
    /// 実日付がその月の日数を超える場合（例: 31日締めの2月）は当月末日に丸める。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="closingDay"/> が <c>0</c>、または <c>1〜31</c>／<c>99</c> の範囲外の場合。
    /// </exception>
    public static DateOnly Resolve(int year, int month, byte closingDay)
    {
        if (closingDay == 0 || (closingDay > 31 && closingDay != 99))
        {
            throw new ArgumentOutOfRangeException(
                nameof(closingDay), closingDay, "締め日は 1〜31 または 99（末日締め）のみ指定できます。");
        }

        var daysInMonth = DateTime.DaysInMonth(year, month);
        var day = closingDay == 99 ? daysInMonth : Math.Min(closingDay, daysInMonth);

        return new DateOnly(year, month, day);
    }
}
