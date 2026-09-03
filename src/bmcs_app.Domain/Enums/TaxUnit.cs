namespace bmcs_app.Domain.Enums;

/// <summary>得意先の消費税計算単位（customer.tax_unit）。</summary>
public enum TaxUnit : byte
{
    /// <summary>外税一括（請求単位）。</summary>
    Invoice = 1,

    /// <summary>外税伝票単位。</summary>
    Slip = 2,

    /// <summary>内税明細単位（都度得意先）。</summary>
    Line = 3,
}
