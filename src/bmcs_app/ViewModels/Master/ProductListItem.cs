namespace bmcs_app.ViewModels.Master;

/// <summary>商品一覧の表示用行。表示文字列の組み立ては Presentation 層の責務。</summary>
public sealed record ProductListItem(
    string ProductCode,
    string ProductName,
    string? ProductNameKana,
    string? UnitName,
    string TaxCategoryDisplay);
