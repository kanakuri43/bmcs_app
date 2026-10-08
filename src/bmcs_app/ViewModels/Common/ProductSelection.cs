using bmcs_app.Domain.Enums;

namespace bmcs_app.ViewModels.Common;

/// <summary>商品検索モーダルから呼び出し元へ転記される1件。</summary>
public sealed record ProductSelection(
    string ProductCode,
    string ProductName,
    string? Specification,
    string? UnitName,
    decimal UnitPrice,
    decimal CostPrice,
    TaxCategory TaxCategory,
    ProductSelectionSource Source);

/// <summary>単価の出どころ。マスタ軸は商品マスタの単価、履歴軸は過去の売上行の単価。</summary>
public enum ProductSelectionSource
{
    Master,
    History,
}
