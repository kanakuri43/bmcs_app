namespace bmcs_app.Domain.Enums;

/// <summary>消費税端数処理の方式（customer.rounding_type）。</summary>
public enum RoundingType : byte
{
    Floor = 1,
    RoundHalfUp = 2,
    Ceiling = 3,
}
