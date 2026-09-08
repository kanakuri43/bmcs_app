using bmcs_app.Application.Common;
using bmcs_app.Domain.Enums;

namespace bmcs_app.ViewModels.Common;

/// <summary>商品検索モーダル「過去の取引履歴から」軸の一覧行。</summary>
public sealed record ProductHistorySearchItem(
    string ProductCode,
    string ProductName,
    string? Specification,
    string? UnitName,
    decimal UnitPrice,
    string LastSlipDateDisplay,
    TaxCategory TaxCategory,
    string TaxCategoryDisplay)
{
    public static ProductHistorySearchItem FromHit(ProductHistoryHit hit) => new(
        hit.ProductCode,
        hit.ProductName,
        hit.Specification,
        hit.UnitName,
        hit.LastUnitPrice,
        hit.LastSlipDate.ToString("yyyy/MM/dd"),
        hit.TaxCategory,
        ProductMasterSearchItem.TaxCategoryDisplayOf(hit.TaxCategory));
}
