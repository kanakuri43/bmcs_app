using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Sales;

/// <summary>
/// 伝票検索モーダル（TODO.md 5-3・5-5・5-6の共通前提）の売上検索結果1件。
/// 明細行1テーブル構成の <c>sales</c> を伝票単位に集約したサマリ（画面には明細行を出さない）。
/// </summary>
public sealed record SalesSlipHit(
    string SalesSlipNumber,
    DateOnly SlipDate,
    string CustomerCode,
    string CustomerName,
    decimal TotalAmount,
    BillingLinkStatus BillingStatus,
    SettlementStatus SettlementStatus);
