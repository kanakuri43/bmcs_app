namespace bmcs_app.Application.Order;

/// <summary>
/// 受注明細行1行に対する売上化済数量の変化分。正＝売上化、負＝売上取消（逆遷移）。
/// <see cref="OrderStatusService.ApplySalesQuantityDeltasAsync"/> の入力。
/// </summary>
public sealed record OrderLineQuantityDelta(string OrderSlipNumber, short LineNumber, decimal QuantityDelta);
