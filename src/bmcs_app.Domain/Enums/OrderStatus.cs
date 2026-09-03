namespace bmcs_app.Domain.Enums;

/// <summary>
/// 受注の進捗状態（order_slip.order_status）。
/// 遷移は docs/product-spec.md「伝票の状態遷移」を参照。
/// </summary>
public enum OrderStatus : byte
{
    NotSold = 1,
    PartiallySold = 2,
    FullySold = 3,
    Cancelled = 4,
}
