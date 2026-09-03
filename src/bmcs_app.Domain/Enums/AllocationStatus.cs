namespace bmcs_app.Domain.Enums;

/// <summary>入金の充当状態（payment_tax_unit_*.allocation_status、detail_payment.allocation_status）。</summary>
public enum AllocationStatus : byte
{
    Unallocated = 1,
    PartiallyAllocated = 2,
    FullyAllocated = 3,
}
