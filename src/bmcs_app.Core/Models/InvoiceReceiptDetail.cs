namespace bmcs_app.Core.Models;

public class InvoiceReceiptDetail
{
    public DateOnly ReceiptDate { get; set; }
    public string   ReceiptNo   { get; set; } = "";
    public string   Remarks     { get; set; } = "";
    public decimal  Amount      { get; set; }
}
