namespace bmcs_app.Core.Models;

public class InvoiceHistorySummary
{
    public DateOnly InvoiceDate   { get; set; }
    public int      CustomerCount { get; set; }

    public string InvoiceDateLabel => InvoiceDate.ToString("yyyy/MM/dd");
}
