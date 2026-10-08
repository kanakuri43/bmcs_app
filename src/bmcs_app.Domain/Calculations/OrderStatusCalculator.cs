using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 受注の進捗状態（order_slip.order_status）を「受注数量」と「売上化済数量」から判定する。
/// docs/product-spec.md「伝票の状態遷移」の受注セクションを参照。
/// </summary>
public static class OrderStatusCalculator
{
    /// <summary>
    /// <see cref="OrderStatus.Cancelled"/> はここでは導出しない。中止は数量から導けない明示的な
    /// 業務操作であり、いちど中止した行は再遷移しない終端状態のため。
    /// </summary>
    public static OrderStatus Determine(decimal orderQuantity, decimal salesConfirmedQuantity)
    {
        if (salesConfirmedQuantity <= 0m)
        {
            return OrderStatus.NotSold;
        }

        if (salesConfirmedQuantity >= orderQuantity)
        {
            return OrderStatus.FullySold;
        }

        return OrderStatus.PartiallySold;
    }
}
