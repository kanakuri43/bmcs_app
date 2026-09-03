namespace bmcs_app.Domain.Enums;

/// <summary>明細請求書の状態（detail_invoice.invoice_status）。</summary>
public enum DetailInvoiceStatus : byte
{
    Issued = 1,
    Cancelled = 2,
}
