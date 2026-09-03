namespace bmcs_app.Domain.Enums;

/// <summary>
/// 請求データの確定状態（billing_tax_unit_*.billing_status）。
/// 売上明細行の請求紐付け状態を表す <see cref="BillingLinkStatus"/> とは別概念。
/// </summary>
public enum BillingStatus : byte
{
    Confirmed = 1,
    Released = 2,
}
