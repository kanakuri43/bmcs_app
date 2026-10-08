using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Order;

/// <summary>
/// 伝票検索モーダルの受注検索結果1件。
/// 明細行1テーブル構成の <c>order_slip</c> を伝票単位に集約したサマリ（画面には明細行を出さない）。
/// </summary>
public sealed record OrderSlipHit(
    string OrderSlipNumber,
    DateOnly OrderDate,
    string CustomerCode,
    string CustomerName,
    decimal TotalAmount,
    OrderStatus OrderStatus);
