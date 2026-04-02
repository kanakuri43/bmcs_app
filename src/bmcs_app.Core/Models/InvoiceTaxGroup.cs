namespace bmcs_app.Core.Models;

public class InvoiceTaxGroup
{
    public byte    TaxRateType    { get; set; }
    public int     TaxTypeId      { get; set; }
    public decimal AppliedTaxRate { get; set; }
    public decimal TaxExcluded    { get; set; }
    public decimal TaxAmount      { get; set; }
}
