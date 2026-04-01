namespace bmcs_app.Core.Models;

public class Product
{
    public int    ProductId   { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int    TaxTypeId   { get; set; }
    public byte   TaxRateType { get; set; }
}
