using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 商品検索モーダル「マスタから」軸の一覧行。商品マスタに仕入先別原価を LEFT JOIN した1行で、
/// 原価がある商品は仕入先ごとの行、無い商品は仕入先なし（標準原価）の1行になる。
/// </summary>
public sealed record ProductMasterSearchItem(
    string ProductCode,
    string ProductName,
    string? ProductNameKana,
    string? Specification,
    string? UnitName,
    decimal UnitPrice,
    string? SupplierCode,
    string? SupplierName,
    decimal CostPrice,
    TaxCategory TaxCategory,
    string TaxCategoryDisplay)
{
    /// <summary>単価は <see cref="bmcs_app.Application.Master.UnitPriceResolver"/> が得意先・伝票日付に応じて決めた値。</summary>
    /// <param name="supplierCost">この行の仕入先別原価。<c>null</c> は仕入先なし（原価は商品マスタの標準原価）。</param>
    public static ProductMasterSearchItem FromEntity(Product product, decimal unitPrice, SelectableSupplierCost? supplierCost) => new(
        product.ProductCode,
        product.ProductName,
        product.ProductNameKana,
        product.Specification,
        product.UnitName,
        unitPrice,
        supplierCost?.SupplierCode,
        supplierCost?.SupplierName,
        supplierCost?.CostPrice ?? product.StandardCostPrice,
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
