namespace bmcs_app.Core.Models;

public class InvoiceHistorySummary
{
    public DateOnly InvoiceDate    { get; set; }
    public byte     ClosingDay     { get; set; }
    public int      CustomerCount  { get; set; }

    public string ClosingDayLabel => ClosingDay is 0 or 99 ? "末日" : $"{ClosingDay}日";
    public string InvoiceDateLabel => InvoiceDate.ToString("yyyy/MM/dd");
}
