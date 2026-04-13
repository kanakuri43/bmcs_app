namespace bmcs_app.Core.Models;

public class PurchaseSlip
{
    public string          PurchaseNo          { get; set; } = "";
    public DateOnly        PurchaseDate        { get; set; }
    public int             SupplierId          { get; set; }
    public string          SupplierCode        { get; set; } = "";
    public string          SupplierName        { get; set; } = "";
    public string?         SupplierPostalCode  { get; set; }
    public string?         SupplierAddress1    { get; set; }
    public string?         SupplierAddress2    { get; set; }
    public int?            PurchaseOrderId     { get; set; }
    public string?         PurchaseOrderNo     { get; set; }
    public int             EmployeeId          { get; set; }
    public string          EmployeeCode        { get; set; } = "";
    public string          EmployeeName        { get; set; } = "";
    public string?         SlipRemarks         { get; set; }
    public bool            IsLocked            { get; set; }
    public DateOnly?       ApClosingAt         { get; set; }
    public List<PurchaseLine> Lines            { get; set; } = new();
}
