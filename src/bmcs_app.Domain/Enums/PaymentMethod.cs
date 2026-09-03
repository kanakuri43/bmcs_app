namespace bmcs_app.Domain.Enums;

/// <summary>入金方法（payment_tax_unit_*.payment_method、detail_payment.payment_method）。</summary>
public enum PaymentMethod : byte
{
    Cash = 1,
    BankTransfer = 2,
    PromissoryNote = 3,
    Offset = 4,
}
