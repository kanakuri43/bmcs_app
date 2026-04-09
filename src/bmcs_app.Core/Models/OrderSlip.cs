namespace bmcs_app.Core.Models;

public class OrderSlip
{
    public string          OrderNo       { get; set; } = "";
    public int             OrderId       { get; set; }
    public DateOnly        OrderDate     { get; set; }
    public int             CustomerId    { get; set; }
    public string          CustomerCode  { get; set; } = "";
    public string          CustomerName  { get; set; } = "";
    public int             EmployeeId    { get; set; }
    public string          EmployeeCode  { get; set; } = "";
    public string          EmployeeName  { get; set; } = "";
    public int             TaxCalcUnitId { get; set; }
    public string?         SlipRemarks   { get; set; }
    public bool            HasSales      { get; set; }
    public List<OrderLine> Lines         { get; set; } = new();
}
