namespace bmcs_app.Core.Models;

public record PaymentLineInput(
    int       LineNo,
    int       PaymentMethodId,
    decimal   Amount,
    string?   LineRemarks,
    DateOnly? BillDueDate);
