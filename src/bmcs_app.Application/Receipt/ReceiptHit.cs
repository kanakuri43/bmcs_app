using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Receipt;

/// <summary>
/// 伝票検索モーダルの入金検索結果1件。
/// 明細行1テーブル構成の <c>receipt</c> を伝票単位に集約したサマリ（画面には明細行を出さない）。
/// </summary>
public sealed record ReceiptHit(
    string ReceiptSlipNumber,
    DateOnly ReceiptDate,
    string CustomerCode,
    string CustomerName,
    decimal ReceiptAmount,
    AllocationStatus AllocationStatus);
