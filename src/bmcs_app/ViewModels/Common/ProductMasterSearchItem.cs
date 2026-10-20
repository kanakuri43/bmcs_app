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
    decimal CostPrice,
    TaxCategory TaxCategory,
    string TaxCategoryDisplay)
{
    /// <summary>単価は <see cref="bmcs_app.Application.Master.UnitPriceResolver"/> が得意先・伝票日付に応じて決めた値。</summary>
    public static ProductMasterSearchItem FromEntity(Product product, decimal unitPrice) => new(
        product.ProductCode,
        product.ProductName,
        product.ProductNameKana,
        product.Specification,
        product.UnitName,
        unitPrice,
        product.StandardCostPrice,
        product.TaxCategory,
        TaxCategoryDisplayOf(product.TaxCategory));

    internal static string TaxCategoryDisplayOf(TaxCategory taxCategory) => taxCategory switch
    {
        TaxCategory.Standard => "通常税率",
        TaxCategory.Reduced => "軽減税率",
        TaxCategory.TaxExempt => "非課税",
        _ => taxCategory.ToString(),
    };
}
