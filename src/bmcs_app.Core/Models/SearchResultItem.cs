namespace bmcs_app.Core.Models;

public class SearchResultItem
{
    public string   SlipType     { get; set; } = "";
    public DateTime SlipDate     { get; set; }
    public string   SlipNo       { get; set; } = "";
    public string   CustomerName { get; set; } = "";
    public decimal  Amount       { get; set; }
    public string   AmountStr    => Amount.ToString("#,##0");
    public string   Status       { get; set; } = "";
    public string   Remarks      { get; set; } = "";
}
