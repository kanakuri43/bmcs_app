namespace bmcs_app.Core.Models;

public class PurchaseLine
{
    public int     LineNo         { get; set; }
    public int     ProductId      { get; set; }
    public string  ProductCode    { get; set; } = "";
    public string  ProductName    { get; set; } = "";
    public decimal Quantity       { get; set; }
    public decimal UnitPrice      { get; set; }
    public decimal CostPrice      { get; set; }
    public int     TaxTypeId      { get; set; }
    public string  TaxTypeName    { get; set; } = "";
    public byte    TaxRateType    { get; set; }
    public decimal AppliedTaxRate { get; set; }
    public decimal LineTaxAmount  { get; set; }
    public string? LineRemarks    { get; set; }
}
