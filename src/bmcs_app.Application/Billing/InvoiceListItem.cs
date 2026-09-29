using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 確定済み請求データ1件分（<see cref="InvoiceService.GetByBillingDateAsync"/>の結果、
/// TODO.md 10-7）。請求締め処理画面の一覧（既に締まっている請求データの照会・再印刷用）に使う。
/// <see cref="BillingClosingTarget"/>（締め処理そのものの候補・プレビュー用）とは用途が異なるため
/// 分離する（<see cref="BillingReleaseTarget"/>と同じ、<c>billing</c>の行から直接組み立てる形）。
/// </summary>
public sealed record InvoiceListItem(
    string BillingNumber,
    string CustomerCode,
    string CustomerName,
    TaxUnit TaxUnit,
    string ClosingYearMonth,
    decimal PreviousBalance,
    decimal ReceiptAmount,
    decimal SalesAmount,
    decimal TaxAmount,
    decimal CurrentBillingAmount,
    DateTime ConfirmedAt);
