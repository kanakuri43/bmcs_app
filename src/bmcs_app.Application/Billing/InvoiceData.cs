using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 請求書1件分の印刷用データ（TODO.md 10-5、締め得意先向け）。WPF 型を含まないプレーンな DTO。
/// 明細は請求期間内の売上ジャーナル（<c>sales.billing_number</c>で紐付く行）から都度組み立てる
/// （`docs/database-schema.md` 2.12節）。複数の売上伝票にまたがるため
/// <see cref="InvoiceLine"/>は<see cref="DetailInvoiceLine"/>と同じく<c>SalesSlipNumber</c>を持つ。
/// </summary>
/// <param name="TaxBreakdowns">
/// 税率別内訳。金額はヘッダーの確定値（<c>billing</c>の固定5カラム）を使い、税率(%)ラベルだけを
/// 明細行から拝借する（<see cref="ConsumptionTaxCalculator.ResolveConfirmedBuckets"/>、
/// TODO.md 10-5設計判断）。
/// </param>
/// <param name="PrintRepresentative">
/// 発行元となる得意先マスタの<c>print_representative_flag</c>。
/// </param>
/// <param name="PrintBankAccounts">
/// <c>bank_account.is_print_on_invoice</c>が真の口座（<c>display_order</c>順）。
/// </param>
public sealed record InvoiceData(
    string BillingNumber,
    DateOnly BillingDate,
    string ClosingYearMonth,
    TaxUnit TaxUnit,
    string CustomerName,
    string? CustomerPostalCode,
    string? CustomerAddress1,
    string? CustomerAddress2,
    CompanyInfo Company,
    bool PrintRepresentative,
    IReadOnlyList<BankAccount> PrintBankAccounts,
    decimal PreviousBalance,
    decimal ReceiptAmount,
    decimal SalesAmount,
    IReadOnlyList<TaxRateBucket> TaxBreakdowns,
    decimal TaxTotal,
    decimal CurrentBillingAmount,
    IReadOnlyList<InvoiceLine> Lines);

/// <summary>請求書の明細行1行分。</summary>
public sealed record InvoiceLine(
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
