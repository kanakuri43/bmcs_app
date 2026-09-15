using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 明細請求書1件分の印刷用データ（TODO.md 10-5）。<see cref="Sales.DeliveryNoteData"/>と同じく
/// WPF 型を含まないプレーンな DTO。明細請求書は複数の売上伝票にまたがるため、
/// <see cref="DetailInvoiceLine"/>は<see cref="Sales.DeliveryNoteLine"/>と異なり
/// <c>SalesSlipNumber</c>を持つ。
/// </summary>
/// <param name="TaxBreakdowns">
/// 税率別内訳。金額はヘッダーの確定値（<c>detail_invoice</c>の固定5カラム）を使い、
/// 税率(%)ラベルだけを明細行から拝借する
/// （<see cref="ConsumptionTaxCalculator.ResolveConfirmedBuckets"/>、TODO.md 10-5設計判断）。
/// </param>
/// <param name="PrintRepresentative">
/// 発行元となる得意先マスタの<c>print_representative_flag</c>。宛名を書き換えても
/// この値は変わらない（`docs/product-spec.md`共通業務ルール3）。
/// </param>
/// <param name="PrintBankAccounts">
/// <c>bank_account.is_print_on_invoice</c>が真の口座（<c>display_order</c>順）。
/// </param>
public sealed record DetailInvoiceData(
    string DetailInvoiceNumber,
    DateOnly IssueDate,
    string CustomerName,
    string AddresseeName,
    string? CustomerPostalCode,
    string? CustomerAddress1,
    string? CustomerAddress2,
    CompanyInfo Company,
    bool PrintRepresentative,
    IReadOnlyList<BankAccount> PrintBankAccounts,
    IReadOnlyList<DetailInvoiceLine> Lines,
    IReadOnlyList<TaxRateBucket> TaxBreakdowns,
    decimal TaxExcludedTotal,
    decimal TaxTotal,
    decimal GrandTotal);

/// <summary>明細請求書の明細行1行分。</summary>
public sealed record DetailInvoiceLine(
    string SalesSlipNumber,
    short LineNumber,
    DateOnly SlipDate,
    SlipType SlipType,
    string ProductCode,
    string ProductName,
    string? Specification,
    string? UnitName,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount,
    TaxCategory TaxCategory,
    decimal TaxRate,
    string? LineRemarks);
