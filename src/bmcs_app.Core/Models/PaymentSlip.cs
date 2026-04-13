namespace bmcs_app.Core.Models;

public class PaymentSlip
{
    public string            PaymentNo          { get; set; } = "";
    public DateOnly          PaymentDate        { get; set; }
    public int               SupplierId         { get; set; }
    public string            SupplierCode       { get; set; } = "";
    public string            SupplierName       { get; set; } = "";
    public string?           SupplierPostalCode { get; set; }
    public string?           SupplierAddress1   { get; set; }
    public string?           SupplierAddress2   { get; set; }
    public string?           SlipRemarks        { get; set; }
    public bool              IsLocked           { get; set; }
    public List<PaymentLine> Lines              { get; set; } = new();
}
