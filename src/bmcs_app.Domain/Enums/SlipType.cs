namespace bmcs_app.Domain.Enums;

/// <summary>売上伝票の種別（sales_tax_unit_*.slip_type）。</summary>
public enum SlipType : byte
{
    Sales = 1,
    Return = 2,
    Discount = 3,
}
