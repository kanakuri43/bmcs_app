namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 税率別内訳。<c>billing_tax_unit_*</c> / <c>detail_invoice</c> の固定5カラム
/// （標準税率対価額・税額、軽減税率対価額・税額、非課税対価額）と1:1で対応する。
/// </summary>
public readonly record struct TaxSummary(
    decimal StandardRateTaxableAmount,
    decimal StandardRateTaxAmount,
    decimal ReducedRateTaxableAmount,
    decimal ReducedRateTaxAmount,
    decimal TaxExemptAmount)
{
    public static TaxSummary Zero => default;

    /// <summary>消費税額合計（<c>billing_tax_unit_*.tax_amount</c> / <c>detail_invoice.tax_amount</c>）。</summary>
    public decimal TaxAmount => StandardRateTaxAmount + ReducedRateTaxAmount;

    /// <summary>税抜対価の額合計（<c>billing_tax_unit_*.sales_amount</c>）。非課税分を含む。</summary>
    public decimal TaxableAmount
        => StandardRateTaxableAmount + ReducedRateTaxableAmount + TaxExemptAmount;

    /// <summary>税込合計（<c>detail_invoice.total_amount</c>）。</summary>
    public decimal TotalAmount => TaxableAmount + TaxAmount;

    /// <summary>
    /// 確定済みの内訳どうしを合算する。端数処理はすでに済んでいる値の合算であり、
    /// ここで再度端数処理を行うわけではない。
    /// </summary>
    public static TaxSummary operator +(TaxSummary a, TaxSummary b) => new(
        a.StandardRateTaxableAmount + b.StandardRateTaxableAmount,
        a.StandardRateTaxAmount + b.StandardRateTaxAmount,
        a.ReducedRateTaxableAmount + b.ReducedRateTaxableAmount,
        a.ReducedRateTaxAmount + b.ReducedRateTaxAmount,
        a.TaxExemptAmount + b.TaxExemptAmount);
}
