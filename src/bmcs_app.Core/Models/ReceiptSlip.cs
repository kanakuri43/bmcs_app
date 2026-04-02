namespace bmcs_app.Core.Models;

public class ReceiptSlip
{
    public string            ReceiptNo    { get; set; } = "";
    public DateOnly          ReceiptDate  { get; set; }
    public int               CustomerId   { get; set; }
    public string            CustomerCode { get; set; } = "";
    public string            CustomerName { get; set; } = "";
    public string?           SlipRemarks  { get; set; }
    public bool              IsLocked     { get; set; }
    public List<ReceiptLine> Lines        { get; set; } = new();
}
