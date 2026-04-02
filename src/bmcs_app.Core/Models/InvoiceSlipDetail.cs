namespace bmcs_app.Core.Models;

public class InvoiceSlipDetail
{
    public DateOnly SaleDate    { get; set; }
    public string   SaleNo      { get; set; } = "";
    public string   Remarks     { get; set; } = "";
    public decimal  TaxExcluded { get; set; }
    public decimal  TaxAmount   { get; set; }
}
