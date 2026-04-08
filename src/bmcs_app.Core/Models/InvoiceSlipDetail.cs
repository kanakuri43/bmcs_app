namespace bmcs_app.Core.Models;

public class InvoiceSlipDetail
{
    public DateOnly SaleDate    { get; set; }
    public string   SaleNo      { get; set; } = "";
    public int      LineNo      { get; set; }
    public string   ProductName { get; set; } = "";
    public decimal  Quantity    { get; set; }
    public decimal  UnitPrice   { get; set; }
    public decimal  LineAmount  { get; set; }
    public string?  LineRemarks { get; set; }
    public byte     TaxRateType { get; set; }  // 1=標準 2=軽減
}
