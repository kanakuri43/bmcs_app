using System.Collections.ObjectModel;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 商品マスタ画面向けの商品検索モーダル。得意先税区分に応じた単価表示や一括転記を前提とする
/// <see cref="ProductSearchDialogViewModel"/>（受注・売上入力向け）とは目的が異なるため分離した。
/// <see cref="CustomerSearchDialogViewModel"/>と同じく、マスタ全件ロード後にメモリで絞り込む。
/// </summary>
public partial class ProductMasterSearchDialogViewModel(ProductService productService)
    : DialogViewModelBase<Product>
{
    private List<Product> _allProducts = [];

    public ObservableCollection<ProductSearchItem> Results { get; } = [];

    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        _allProducts = await productService.GetProductsAsync();
        ApplyFilter();
    });

    partial void OnSearchKeywordChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var keywords = SearchKeyword
            .Split(' ', '　')
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToArray();

        var filtered = keywords.Length == 0
            ? _allProducts
            : _allProducts.Where(p => keywords.All(k => Matches(p, k)));

        Results.Clear();
        foreach (var product in filtered)
        {
            Results.Add(ProductSearchItem.FromEntity(product));
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    private static bool Matches(Product product, string keyword) =>
        Contains(product.ProductCode, keyword)
        || Contains(product.ProductName, keyword)
        || Contains(product.ProductNameKana, keyword)
        || Contains(product.Specification, keyword);

    private static bool Contains(string? source, string keyword) =>
        source is not null && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>一覧で選択した商品を確定する（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private void Confirm(object? item)
    {
        if (item is not ProductSearchItem searchItem)
        {
            return;
        }

        var product = _allProducts.SingleOrDefault(p => p.ProductCode == searchItem.ProductCode);
        if (product is not null)
        {
            CloseWith(product);
        }
    }
}
