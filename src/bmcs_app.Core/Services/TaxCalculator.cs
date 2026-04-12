using bmcs_app.Core.Models;

namespace bmcs_app.Core.Services;

/// <summary>税計算に必要な明細行の入力値</summary>
public readonly record struct TaxLineInput(
    int     TaxTypeId,
    decimal AppliedTaxRate,
    decimal LineAmount,
    decimal LineTaxAmount);

/// <summary>
/// 消費税計算ロジック（Sales / Order で共通）。
/// 全メソッドは static で副作用なし。
/// </summary>
public static class TaxCalculator
{
    /// <summary>taxCalcUnitId=1 が明細単位、2 が伝票単位</summary>
    public static bool IsLineTaxCalc(int taxCalcUnitId) => taxCalcUnitId == 1;

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
    /// 行税額を計算する。
    /// isLineTaxCalc=false（伝票単位）または taxRate=0 の場合は 0。
    /// 内税(taxTypeId=2): floor(金額 × rate ÷ (1+rate))
    /// 外税(taxTypeId=1): floor(金額 × rate)
    /// </summary>
    public static decimal CalcLineTaxAmount(
        decimal lineAmount, decimal appliedTaxRate, int taxTypeId, bool isLineTaxCalc)
    {
        if (!isLineTaxCalc || appliedTaxRate == 0) return 0m;
        if (taxTypeId == 2)
            return Math.Floor(lineAmount * appliedTaxRate / (1 + appliedTaxRate));
        return Math.Floor(lineAmount * appliedTaxRate);
    }

    /// <summary>
    /// 外税合計を計算する。
    /// isLineTaxCalc=true なら各行の LineTaxAmount を合計。
    /// false（伝票単位）なら税率ごとに LineAmount を合計してから計算。
    /// </summary>
    public static decimal CalcExternalTaxTotal(IEnumerable<TaxLineInput> lines, bool isLineTaxCalc)
    {
        var ext = lines.Where(l => l.TaxTypeId == 1 && l.AppliedTaxRate > 0);
        if (isLineTaxCalc) return ext.Sum(l => l.LineTaxAmount);
        return ext
            .GroupBy(l => l.AppliedTaxRate)
            .Sum(g => Math.Floor(g.Sum(l => l.LineAmount) * g.Key));
    }

    /// <summary>
    /// 内税合計を計算する。
    /// isLineTaxCalc=true なら各行の LineTaxAmount を合計。
    /// false（伝票単位）なら税率ごとに LineAmount を合計してから計算。
    /// </summary>
    public static decimal CalcInternalTaxTotal(IEnumerable<TaxLineInput> lines, bool isLineTaxCalc)
    {
        var intn = lines.Where(l => l.TaxTypeId == 2 && l.AppliedTaxRate > 0);
        if (isLineTaxCalc) return intn.Sum(l => l.LineTaxAmount);
        return intn
            .GroupBy(l => l.AppliedTaxRate)
            .Sum(g => Math.Floor(g.Sum(l => l.LineAmount) * g.Key / (1 + g.Key)));
    }
}
