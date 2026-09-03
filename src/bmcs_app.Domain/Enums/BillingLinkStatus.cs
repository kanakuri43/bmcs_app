namespace bmcs_app.Domain.Enums;

/// <summary>
/// 売上明細行の請求への紐付け状態（sales_tax_unit_*.billing_status）。
/// 請求データ自体の確定状態を表す <see cref="BillingStatus"/> とは別概念。
/// </summary>
public enum BillingLinkStatus : byte
{
    Unbilled = 1,
    Billed = 2,
}
