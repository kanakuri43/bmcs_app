using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 締め解除処理（請求日単位、TODO.md 6-2 2026-09-15改訂）の請求データ1件分。
/// <see cref="BillingReleaseService.PreviewAsync"/>／<see cref="BillingReleaseService.ReleaseByBillingDateAsync"/>
/// の両方がこの形で結果を返す（<see cref="BillingClosingTarget"/>と同じく、金額を出す経路を1本にする）。
/// </summary>
/// <param name="BlockReason">
/// 非null＝この請求データは締め順序の逆転（より新しい確定済み<c>billing</c>が存在する）等により
/// 解除できない。<see cref="BillingClosingTarget.SkipReason"/>と異なり、1件でも非nullなら
/// 指定した請求日の解除処理全体を中止する（All-or-nothing）。
/// </param>
public sealed record BillingReleaseTarget(
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
    DateTime ConfirmedAt,
    string ConfirmedBy,
    string? BlockReason);
