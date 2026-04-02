namespace bmcs_app.Core.Models;

public record ReceiptLineInput(
    int     LineNo,
    int     PaymentMethodId,
    decimal Amount,
    string? LineRemarks
);
