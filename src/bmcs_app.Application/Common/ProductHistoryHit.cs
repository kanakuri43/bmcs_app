using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Common;

/// <summary>
/// 商品検索モーダル（TODO.md 3-2）の「過去の取引履歴から」軸の1件。
/// 得意先ごとに商品別の最新売上行から作る。
/// </summary>
public sealed record ProductHistoryHit(
    string ProductCode,
    string ProductName,
    string? Specification,
    string? UnitName,
    DateOnly LastSlipDate,
    decimal LastUnitPrice,
    TaxCategory TaxCategory);
