namespace bmcs_app.Core.Models;

public class InvoiceHeader
{
    public int      CustomerId            { get; set; }
    public string   CustomerName          { get; set; } = "";
    public string?  CustomerPostalCode    { get; set; }
    public string?  CustomerAddress1      { get; set; }
    public string?  CustomerAddress2      { get; set; }
    public DateOnly InvoiceDate           { get; set; }
    public decimal  PreviousInvoiceAmount { get; set; }
    public decimal  ReceiptAmount         { get; set; }
    public decimal  SalesAmountStandard   { get; set; }
    public decimal  SalesAmountReduced    { get; set; }
    public decimal  TaxAmountStandard     { get; set; }
    public decimal  TaxAmountReduced      { get; set; }
    public decimal  CurrentInvoiceAmount  { get; set; }
}
