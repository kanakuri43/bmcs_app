namespace bmcs_app.Core.Models;

public record SaleLineInput(
    int     LineNo,
    int     ProductId,
    string  ProductCode,
    string  ProductName,
    decimal Quantity,
    decimal UnitPrice,
    int     TaxTypeId,
    byte    TaxRateType,
    decimal AppliedTaxRate,
    decimal LineTaxAmount,
    decimal SlipTaxAmount,
    string? LineRemarks
);
