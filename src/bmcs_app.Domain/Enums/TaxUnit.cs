namespace bmcs_app.Domain.Enums;

/// <summary>得意先の消費税計算単位（customer.tax_unit）。</summary>
public enum TaxUnit : byte
{
    /// <summary>請求単位（外税）。</summary>
    Invoice = 1,

    /// <summary>伝票単位（外税）。</summary>
    Slip = 2,

    /// <summary>内税明細単位（都度得意先）。</summary>
    Line = 3,
}
