namespace bmcs_app.Core.Models;

public class PaymentMethod
{
    public int    PaymentMethodId   { get; set; }
    public string PaymentMethodCode { get; set; } = "";
    public string PaymentMethodName { get; set; } = "";
}
