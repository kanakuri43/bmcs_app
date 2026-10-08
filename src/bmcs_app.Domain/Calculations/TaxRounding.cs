using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 消費税額の端数処理。金額に対して <see cref="Math.Round(decimal)"/> /
/// <see cref="Math.Floor(decimal)"/> / <see cref="Math.Ceiling(decimal)"/> を直接呼んでよいのは
/// このクラスだけとする（端数処理のルールを1箇所に集約するため）。
/// </summary>
public static class TaxRounding
{
    /// <summary>
    /// 1円単位に端数処理する。
    /// マイナスの値は<b>絶対値を丸めて符号を戻す</b>（sign-symmetric）。
    /// これにより、同額の返品・値引は元の売上の税額をちょうど打ち消す
    /// （例: 切捨で税抜1,005円@10%の売上の税額は100円。同額を返品したときの税額は
    /// 単純に <see cref="Math.Floor(decimal)"/> を使うと-101円になり1円の差が残るが、
    /// 絶対値を丸めて符号を戻すことで-100円になり差が残らない）。
    /// </summary>
    public static decimal RoundToYen(decimal value, RoundingType roundingType)
    {
        return roundingType switch
        {
            RoundingType.Floor => decimal.Truncate(value),
            RoundingType.RoundHalfUp => Math.Round(value, 0, MidpointRounding.AwayFromZero),
            RoundingType.Ceiling => value >= 0m ? Math.Ceiling(value) : Math.Floor(value),
            _ => throw new ArgumentOutOfRangeException(
                nameof(roundingType), roundingType, "未対応の端数区分です。"),
        };
    }
}
