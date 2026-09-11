using bmcs_app.Domain.Entities;

namespace bmcs_app.ViewModels.Common;

/// <summary>商品マスタ検索モーダルの一覧行の表示用。表示文字列の組み立ては Presentation 層の責務。</summary>
public sealed record ProductSearchItem(
    string ProductCode,
    string ProductName,
    string? ProductNameKana,
    string? Specification,
    string? UnitName)
{
    public static ProductSearchItem FromEntity(Product product) => new(
        product.ProductCode,
        product.ProductName,
        product.ProductNameKana,
        product.Specification,
        product.UnitName);
}
