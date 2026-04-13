namespace bmcs_app.Core.Models;

public class PurchaseOrderSlip
{
    public string                   PurchaseOrderNo   { get; set; } = "";
    public int                      PurchaseOrderId   { get; set; }
    public DateOnly                 PurchaseOrderDate { get; set; }
    public int                      SupplierId        { get; set; }
    public string                   SupplierCode      { get; set; } = "";
    public string                   SupplierName      { get; set; } = "";
    public int                      EmployeeId        { get; set; }
    public string                   EmployeeCode      { get; set; } = "";
    public string                   EmployeeName      { get; set; } = "";
    public int                      TaxCalcUnitId     { get; set; }
    public string?                  SlipRemarks       { get; set; }
    public bool                     HasPurchases      { get; set; }
    public List<PurchaseOrderLine>  Lines             { get; set; } = new();
}
