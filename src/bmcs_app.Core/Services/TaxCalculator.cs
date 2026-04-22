using bmcs_app.Core.Models;

namespace bmcs_app.Core.Services;

/// <summary>税計算に必要な明細行の入力値</summary>
public readonly record struct TaxLineInput(decimal AppliedTaxRate, decimal LineAmount);

/// <summary>
/// 消費税計算ロジック（Sales / Order / Purchase / PurchaseOrder で共通）。
/// 全メソッドは static で副作用なし。外税のみ・伝票単位の割戻し計算。
/// </summary>
public static class TaxCalculator
{
    /// <summary>日付と税率タイプから適用税率を求める。対象期間なし → 0</summary>
    public static decimal GetAppliedTaxRate(
        IEnumerable<TaxRatePeriod> periods, byte taxRateType, DateOnly date)
    {
        var period = periods
            .Where(p => p.StartDate <= date && (p.EndDate is null || p.EndDate >= date))
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefault();
        if (period is null) return 0m;
        return taxRateType switch
        {
            1 => period.PrimaryTaxRate,
            2 => period.SecondaryTaxRate,
            3 => period.TertiaryTaxRate ?? 0m,
            _ => 0m,
        };
    }

    /// <summary>
    /// 外税合計を計算する。税率ごとに LineAmount を集計し、1回の端数処理を行う（インボイス準拠）。
    /// taxFractionId: 1=切捨（デフォルト）/ 2=切上 / 3=四捨五入
    /// </summary>
    public static decimal CalcExternalTaxTotal(IEnumerable<TaxLineInput> lines, int taxFractionId = 1)
        => lines.Where(l => l.AppliedTaxRate > 0)
            .GroupBy(l => l.AppliedTaxRate)
            .Sum(g => ApplyRounding(g.Sum(l => l.LineAmount) * g.Key, taxFractionId));

    private static decimal ApplyRounding(decimal value, int taxFractionId) => taxFractionId switch
    {
        2 => Math.Ceiling(value),
        3 => Math.Round(value, 0, MidpointRounding.AwayFromZero),
        _ => Math.Floor(value),
    };
}
