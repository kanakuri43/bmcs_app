using bmcs_app.Domain.Enums;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 商品検索モーダル「過去の取引履歴から」軸の一覧行。仕入先は前回の売上行の仕入先で、
/// 原価は現在の仕入先別原価（無ければ標準原価）。前回の仕入先に有効な原価が無いときは仕入先なしになる。
/// </summary>
public sealed record ProductHistorySearchItem(
    string ProductCode,
    string ProductName,
    string? Specification,
    string? UnitName,
    decimal UnitPrice,
    string? SupplierCode,
    string? SupplierName,
    decimal CostPrice,
    string LastSlipDateDisplay,
    TaxCategory TaxCategory,
    string TaxCategoryDisplay);
