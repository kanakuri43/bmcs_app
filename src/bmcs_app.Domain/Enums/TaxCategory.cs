namespace bmcs_app.Domain.Enums;

/// <summary>
/// 税種別区分（product.tax_category、order_slip.tax_category、sales_tax_unit_*.tax_category）。
/// billing_tax_unit_*.tax_exempt_amount（非課税）の対応と揃える。
/// 具体的な税率（%）は持たない。実際の税率は税率マスタ（tax_rate_master）から引く。
/// </summary>
public enum TaxCategory : byte
{
    /// <summary>標準税率。</summary>
    Standard = 1,

    /// <summary>軽減税率。</summary>
    Reduced = 2,

    /// <summary>非課税。</summary>
    TaxExempt = 3,
}
