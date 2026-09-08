using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 税率マスタ（<see cref="TaxRateMaster"/>）から、伝票日付・税種別区分に対応する税率を解決する。
/// 削除済み（<c>is_deleted</c>）レコードの除外は永続化の関心事のため、この解決ロジックの
/// 責務ではなく、呼び出し側（Application層）が担う。
/// </summary>
public static class TaxRateResolver
{
    /// <summary>
    /// 伝票日付以前で最も新しい <see cref="TaxRateMaster.EffectiveDate"/> のレコードを返す
    /// （該当なしは null）。境界は「以前」に伝票日付当日を含む（inclusive）。
    /// 入力の並び順には依存しない。
    /// </summary>
    public static TaxRateMaster? FindApplicable(
        IReadOnlyList<TaxRateMaster> taxRateMasters, DateOnly slipDate)
    {
        return taxRateMasters
            .Where(m => m.EffectiveDate <= slipDate)
            .OrderByDescending(m => m.EffectiveDate)
            .FirstOrDefault();
    }

    /// <summary>
    /// 税種別区分に対応する税率（%）。非課税はマスタを参照せず0を返す
    /// （<c>docs/database-schema.md</c> 2.3節「非課税は本マスタを参照しない」）。
    /// 標準・軽減で該当レコードが無い場合は例外にする（黙って0を返すと税額が消える）。
    /// </summary>
    public static decimal ResolveRate(
        IReadOnlyList<TaxRateMaster> taxRateMasters, DateOnly slipDate, TaxCategory taxCategory)
    {
        if (taxCategory == TaxCategory.TaxExempt)
        {
            return 0m;
        }

        var applicable = FindApplicable(taxRateMasters, slipDate)
            ?? throw new InvalidOperationException(
                $"伝票日付「{slipDate:yyyy/MM/dd}」に適用できる税率マスタが見つかりません。");

        return taxCategory switch
        {
            TaxCategory.Standard => applicable.StandardTaxRate,
            TaxCategory.Reduced => applicable.ReducedTaxRate,
            _ => throw new ArgumentOutOfRangeException(
                nameof(taxCategory), taxCategory, "未対応の税種別区分です。"),
        };
    }
}
