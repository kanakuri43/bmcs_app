using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.ViewModels.Common;

/// <summary>商品検索モーダル「マスタから」軸の一覧行。</summary>
public sealed record ProductMasterSearchItem(
    string ProductCode,
    string ProductName,
    string? ProductNameKana,
    string? Specification,
    string? UnitName,
    decimal UnitPrice,
    TaxCategory TaxCategory,
    string TaxCategoryDisplay)
{
    public static ProductMasterSearchItem FromEntity(Product product) => new(
        product.ProductCode,
        product.ProductName,
        product.ProductNameKana,
        product.Specification,
        product.UnitName,
        product.StandardUnitPriceExclTax,
        product.TaxCategory,
        TaxCategoryDisplayOf(product.TaxCategory));

    internal static string TaxCategoryDisplayOf(TaxCategory taxCategory) => taxCategory switch
    {
        TaxCategory.Standard => "課税10%",
        TaxCategory.Reduced => "軽減8%",
        TaxCategory.TaxExempt => "非課税",
        _ => taxCategory.ToString(),
    };
}
