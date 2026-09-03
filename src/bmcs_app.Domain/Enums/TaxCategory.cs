namespace bmcs_app.Domain.Enums;

/// <summary>
/// 税種別区分（product.tax_category、order_slip.tax_category、sales_tax_unit_*.tax_category）。
/// billing_tax_unit_*.tax_exempt_amount（非課税）の対応と揃える。
/// </summary>
public enum TaxCategory : byte
{
    /// <summary>課税10%。</summary>
    Standard10 = 1,

    /// <summary>軽減税率8%。</summary>
    Reduced8 = 2,

    /// <summary>非課税。</summary>
    TaxExempt = 3,
}
