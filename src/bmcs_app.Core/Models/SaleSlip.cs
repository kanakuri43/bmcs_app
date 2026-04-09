namespace bmcs_app.Core.Models;

public class SaleSlip
{
    public string         SaleNo       { get; set; } = "";
    public DateOnly       SaleDate     { get; set; }
    public int            CustomerId   { get; set; }
    public string         CustomerCode       { get; set; } = "";
    public string         CustomerName       { get; set; } = "";
    public string?        CustomerPostalCode { get; set; }
    public string?        CustomerAddress1   { get; set; }
    public string?        CustomerAddress2   { get; set; }
    public int?           OrderId      { get; set; }
    public string?        OrderNo      { get; set; }
    public int            EmployeeId   { get; set; }
    public string         EmployeeCode { get; set; } = "";
    public string         EmployeeName { get; set; } = "";
    public string?        SlipRemarks  { get; set; }
    public bool           IsLocked        { get; set; }
    public DateOnly?      InvoicedAt      { get; set; }
    public DateOnly?      ArAggregatedAt  { get; set; }
    public List<SaleLine> Lines           { get; set; } = new();
}
