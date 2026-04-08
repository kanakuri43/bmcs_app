namespace bmcs_app.Core.Models;

public class InvoiceReceiptDetail
{
    public DateOnly ReceiptDate       { get; set; }
    public string   ReceiptNo         { get; set; } = "";
    public int      LineNo            { get; set; }
    public string   PaymentMethodName { get; set; } = "";
    public decimal  Amount            { get; set; }
    public string?  LineRemarks       { get; set; }
}
