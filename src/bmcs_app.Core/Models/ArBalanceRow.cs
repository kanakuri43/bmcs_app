namespace bmcs_app.Core.Models;

public class ArBalanceRow
{
    public string  CustomerCode        { get; set; } = "";
    public string  CustomerName        { get; set; } = "";
    public decimal CarriedOverAmount   { get; set; }
    public decimal SalesAmountStandard { get; set; }
    public decimal SalesAmountReduced  { get; set; }
    public decimal TaxAmountStandard   { get; set; }
    public decimal TaxAmountReduced    { get; set; }
    public decimal ReceiptAmount       { get; set; }
    public decimal ClosingAmount       { get; set; }
}
