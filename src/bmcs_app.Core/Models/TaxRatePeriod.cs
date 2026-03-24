namespace bmcs_app.Core.Models;

public class TaxRatePeriod
{
    public int      TaxRatePeriodId  { get; set; }
    public DateOnly StartDate        { get; set; }
    public DateOnly? EndDate         { get; set; }
    public decimal  PrimaryTaxRate   { get; set; }
    public decimal  SecondaryTaxRate { get; set; }
    public decimal? TertiaryTaxRate  { get; set; }
}
