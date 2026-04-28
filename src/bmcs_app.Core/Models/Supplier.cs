namespace bmcs_app.Core.Models;

public class Supplier
{
    public int     SupplierId     { get; set; }
    public string  SupplierCode   { get; set; } = "";
    public string  SupplierName   { get; set; } = "";
    public byte    ClosingDay     { get; set; }
    public int     TaxFractionId  { get; set; }
    public int     TaxCalcUnitId  { get; set; }
    public int?    EmployeeId     { get; set; }
    public string? PostalCode     { get; set; }
    public string? Address1       { get; set; }
    public string? Address2       { get; set; }
    public string? InvoiceNo        { get; set; }
    public bool    IsMiscellaneous  { get; set; }
}
