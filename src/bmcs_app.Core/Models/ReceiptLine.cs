namespace bmcs_app.Core.Models;

public class ReceiptLine
{
    public int     LineNo            { get; set; }
    public int     PaymentMethodId   { get; set; }
    public string  PaymentMethodName { get; set; } = "";
    public decimal Amount            { get; set; }
    public string? LineRemarks       { get; set; }
    public DateOnly? BillDueDate     { get; set; }
}
