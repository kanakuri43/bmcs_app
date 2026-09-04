namespace bmcs_app.Domain.Enums;

/// <summary>入金方法（receipt_tax_unit_*.receipt_method、detail_receipt.receipt_method）。</summary>
public enum ReceiptMethod : byte
{
    Cash = 1,
    BankTransfer = 2,
    PromissoryNote = 3,
    Offset = 4,
}
