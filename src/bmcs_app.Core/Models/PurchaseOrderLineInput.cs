namespace bmcs_app.Core.Models;

public record PurchaseOrderLineInput(
    int     LineNo,
    int     ProductId,
    string  ProductCode,
    string  ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal CostPrice,
    int     TaxTypeId,
    byte    TaxRateType,
    decimal AppliedTaxRate,
    decimal LineTaxAmount,
    decimal SlipTaxAmount,
    string? LineRemarks);
