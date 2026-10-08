using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 明細請求書発行における売上明細行1件分の表示用データ。
/// <see cref="DetailInvoiceService.GetCandidatesAsync"/>（取込候補）と
/// <see cref="DetailInvoiceService.GetByNumberAsync"/>（発行済みの明細請求書の内訳）の
/// 両方で同じ形を使う（候補行と発行済み行で構造が同一のため、型を分けない）。
/// <see cref="Amount"/>・<see cref="TaxAmount"/>は<c>tax_unit = 3</c>（内税明細単位）の
/// <c>sales</c>行の値であり、<see cref="Amount"/>は税込金額。
/// </summary>
public sealed record DetailInvoiceSalesLineItem(
    string SalesSlipNumber,
    short SalesLineNumber,
    DateOnly SlipDate,
    SlipType SlipType,
    string ProductCode,
    string ProductName,
    string? Specification,
    string? UnitName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount,
    decimal TaxAmount,
    TaxCategory TaxCategory,
    decimal TaxRate,
    string? LineRemarks);
