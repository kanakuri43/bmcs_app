namespace bmcs_app.Domain.Enums;

/// <summary>入金消込の状態（sales_tax_unit_*.settlement_status）。</summary>
public enum SettlementStatus : byte
{
    Unsettled = 1,
    PartiallySettled = 2,
    FullySettled = 3,
}
