namespace bmcs_app.Core.Models;

public class ArHistorySummary
{
    public DateOnly ClosingDate   { get; set; }
    public int      CustomerCount { get; set; }

    public string ClosingDateLabel => ClosingDate.ToString("yyyy/MM/dd");
}
