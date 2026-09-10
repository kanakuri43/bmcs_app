using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 伝票区分（<see cref="SlipType"/>）に応じた数量の符号・原価の正規化（TODO.md 5-4）。
/// ユーザーは常に正の数量を入力し、符号は区分から機械的に決まる（符号の入力ミスを構造的に防ぐ）。
/// 金額が狂う経路を1箇所に閉じ込めるため、<c>SalesService</c>・ViewModel の双方がこのクラスだけを経由する。
/// </summary>
public static class SalesSlipTypeRules
{
    /// <summary>
    /// 数量の符号を区分に合わせて正規化する。売上は正、返品・値引は負（絶対値は変えない）。
    /// </summary>
    public static decimal NormalizeQuantity(SlipType slipType, decimal quantity) =>
        slipType == SlipType.Sales ? Math.Abs(quantity) : -Math.Abs(quantity);

    /// <summary>
    /// 原価を区分に合わせて正規化する。
    /// 値引は現品の移動を伴わないため原価は常に0（0でないと粗利計算の符号が反転する）。
    /// 売上・返品は商品原価をそのまま使う（返品は原価もマイナス計上され、元の売上の粗利影響を打ち消す）。
    /// </summary>
    public static decimal NormalizeCostPrice(SlipType slipType, decimal productCostPrice) =>
        slipType == SlipType.Discount ? 0m : productCostPrice;
}
