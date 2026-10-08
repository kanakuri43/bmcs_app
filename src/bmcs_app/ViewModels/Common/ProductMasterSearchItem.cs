using bmcs_app.Domain.Calculations;
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
    /// <summary>
    /// 単価は得意先の税区分（<paramref name="taxUnit"/>）に応じて外税／内税を選ぶ
    /// （<see cref="IUnitPriceCalculator"/>）。
    /// </summary>
    public static ProductMasterSearchItem FromEntity(
        Product product, TaxUnit taxUnit, IUnitPriceCalculator unitPriceCalculator) => new(
        product.ProductCode,
        product.ProductName,
        product.ProductNameKana,
        product.Specification,
        product.UnitName,
        unitPriceCalculator.SelectStandardUnitPrice(product, taxUnit),
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
