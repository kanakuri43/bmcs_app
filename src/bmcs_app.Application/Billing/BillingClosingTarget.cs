using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 請求締め処理（TODO.md 6-1）の得意先1件分の集計結果。
/// <see cref="BillingClosingService.PreviewAsync"/>／<see cref="BillingClosingService.ConfirmAsync"/>
/// の両方がこの形で結果を返す（金額を出す経路を1本にする。docs/design_document.md 9章）。
/// </summary>
/// <param name="SkipReason">
/// 非null＝この得意先は請求データを作らなかった（二重締め防止・締め順序の逆転防止・
/// 対象データなしのいずれか）。理由は画面にそのまま表示する。
/// </param>
/// <param name="BillingNumber"><see cref="BillingClosingService.ConfirmAsync"/> で確定した場合のみ設定される。</param>
public sealed record BillingClosingTarget(
    string CustomerCode,
    string CustomerName,
    TaxUnit TaxUnit,
    decimal PreviousBalance,
    decimal ReceiptAmount,
    decimal SalesAmount,
    decimal TaxAmount,
    decimal CurrentBillingAmount,
    decimal StandardRateTaxableAmount,
    decimal StandardRateTaxAmount,
    decimal ReducedRateTaxableAmount,
    decimal ReducedRateTaxAmount,
    decimal TaxExemptAmount,
    string? SkipReason,
    string? BillingNumber);
