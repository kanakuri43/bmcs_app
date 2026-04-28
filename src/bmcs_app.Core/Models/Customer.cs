namespace bmcs_app.Core.Models;

public class Customer
{
    public int    CustomerId     { get; set; }
    public string CustomerCode   { get; set; } = "";
    public string CustomerName   { get; set; } = "";
    public byte   ClosingDay     { get; set; }
    public int    TaxFractionId  { get; set; }
    public int    TaxCalcUnitId  { get; set; }
    public int?    EmployeeId     { get; set; }
    public string? PostalCode     { get; set; }
    public string? Address1       { get; set; }
    public string? Address2          { get; set; }
    public bool    IsMiscellaneous   { get; set; }
}
