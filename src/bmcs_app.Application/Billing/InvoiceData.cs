using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 請求書1件分の印刷用データ（締め得意先向け）。WPF 型を含まないプレーンな DTO。
/// 明細は請求期間内の売上ジャーナル（<c>sales.billing_number</c>で紐付く行）から都度組み立てる
/// （`docs/database-schema.md` 2.12節）。複数の売上伝票にまたがるため
/// <see cref="InvoiceLine"/>は<see cref="DetailInvoiceLine"/>と同じく<c>SalesSlipNumber</c>を持つ。
/// </summary>
/// <param name="TaxBreakdowns">
/// 税率別内訳。金額はヘッダーの確定値（<c>billing</c>の固定5カラム）を使い、税率(%)ラベルだけを
/// 明細行から拝借する（<see cref="ConsumptionTaxCalculator.ResolveConfirmedBuckets"/>、
/// ）。
/// </param>
/// <param name="PrintRepresentative">
/// 発行元となる得意先マスタの<c>print_representative_flag</c>。
/// </param>
/// <param name="BillingBankAccounts">
/// 発行元の得意先（請求集約先。<see cref="CustomerCode"/>）に紐づいた振込先口座
/// （<c>customers.bank_account_code1</c>／<c>bank_account_code2</c>、0〜2件、スロット順。
/// 論理削除済みの口座は除外する）。
/// </param>
/// <param name="CustomerCode">
/// <c>billing.customer_code</c>（請求データは請求集約先にしか作られないため、この値は常に
/// 請求集約先または単独得意先自身のコード）。
/// </param>
public sealed record InvoiceData(
    string BillingNumber,
    DateOnly BillingDate,
    string ClosingYearMonth,
    TaxUnit TaxUnit,
    string CustomerCode,
    string CustomerName,
    string? CustomerPostalCode,
    string? CustomerAddress1,
    string? CustomerAddress2,
    CompanyInfo Company,
    bool PrintRepresentative,
    IReadOnlyList<BankAccount> BillingBankAccounts,
    decimal PreviousBalance,
    decimal ReceiptAmount,
    decimal SalesAmount,
    IReadOnlyList<TaxRateBucket> TaxBreakdowns,
    decimal TaxTotal,
    decimal CurrentBillingAmount,
    IReadOnlyList<InvoiceLine> Lines);

/// <summary>
/// 請求書の明細行1行分。<see cref="CustomerCode"/>／<see cref="CustomerName"/>は伝票単位の値
/// （<c>sales.customer_code</c>／<c>customer_name</c>のスナップショット）で、単独得意先の請求書では
/// ヘッダーの<see cref="InvoiceData.CustomerName"/>と常に一致するが、請求集約先の請求書では
/// 明細行ごとに異なりうる（請求集約元の分も合算されているため）。
/// </summary>
public sealed record InvoiceLine(
    string SalesSlipNumber,
    short LineNumber,
    DateOnly SlipDate,
    string CustomerCode,
    string CustomerName,
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
