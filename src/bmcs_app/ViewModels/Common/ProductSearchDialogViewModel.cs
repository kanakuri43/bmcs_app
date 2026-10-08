using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Common;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 商品検索モーダル。「マスタから」「過去の取引履歴から」の2軸を持ち、
/// 選択した商品を最大 <see cref="MaxBasketSize"/> 件まで一括転記できる。
/// 履歴軸は対象得意先が選択されていない場合は無効化する。
/// </summary>
public partial class ProductSearchDialogViewModel(
    ProductService productService,
    ProductHistoryQueryService historyQueryService,
    IUnitPriceCalculator unitPriceCalculator)
    : DialogViewModelBase<IReadOnlyList<ProductSelection>>
{
    public const int MaxBasketSize = 6;

    private List<Product> _allProducts = [];
    private List<ProductHistoryHit> _historyHits = [];
    private bool _historyLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomer))]
    [NotifyPropertyChangedFor(nameof(ShowCustomerRequiredNotice))]
    public partial string? TargetCustomerCode { get; set; }

    /// <summary>
    /// 対象得意先の税区分。マスタ軸の単価列（外税／内税どちらを転記するか）を決める
    /// 呼び出し元が <see cref="TargetCustomerCode"/> と一緒に設定する。
    /// 未設定（得意先未選択）のときは外税単価を使う（請求単位・伝票単位が多数派のため）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MasterUnitPriceHeader))]
    public partial TaxUnit? TargetTaxUnit { get; set; }

    /// <summary>マスタ軸の単価列ヘッダ。表示と転記される単価が食い違わないよう、転記元と同じ判定を使う。</summary>
    public string MasterUnitPriceHeader => TargetTaxUnit == TaxUnit.Line ? "単価(税込)" : "単価(税抜)";

    /// <summary>履歴軸を有効化できるかどうか（対象得意先が指定されているか）。</summary>
    public bool HasCustomer => !string.IsNullOrWhiteSpace(TargetCustomerCode);

    /// <summary>履歴タブに「先に得意先を選択してください」を表示するかどうか。</summary>
    public bool ShowCustomerRequiredNotice => !HasCustomer;

    [ObservableProperty]
    public partial string MasterSearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HistorySearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int MasterSelectedIndex { get; set; } = -1;

    [ObservableProperty]
    public partial int HistorySelectedIndex { get; set; } = -1;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public ObservableCollection<ProductMasterSearchItem> MasterResults { get; } = [];

    public ObservableCollection<ProductHistorySearchItem> HistoryResults { get; } = [];

    public ObservableCollection<ProductSelection> Basket { get; } = [];

    [RelayCommand]
    private Task LoadMasterAsync() => RunBusyAsync(async () =>
    {
        _allProducts = await productService.GetProductsAsync();
        ApplyMasterFilter();
    });

    /// <summary>
    /// 履歴タブが最初に表示されたときに1回だけ問い合わせる（タブ切替のたびに再クエリしない）。
    /// </summary>
    [RelayCommand]
    private Task LoadHistoryAsync() => RunBusyAsync(async () =>
    {
        if (_historyLoaded || !HasCustomer)
        {
            return;
        }

        _historyHits = await historyQueryService.SearchAsync(TargetCustomerCode!);
        _historyLoaded = true;
        ApplyHistoryFilter();
    });

    partial void OnMasterSearchKeywordChanged(string value) => ApplyMasterFilter();

    partial void OnHistorySearchKeywordChanged(string value) => ApplyHistoryFilter();

    private void ApplyMasterFilter()
    {
        var keywords = SplitKeywords(MasterSearchKeyword);

        var filtered = keywords.Length == 0
            ? _allProducts
            : _allProducts.Where(p => keywords.All(k =>
                Contains(p.ProductCode, k)
                || Contains(p.ProductName, k)
                || Contains(p.ProductNameKana, k)
                || Contains(p.Specification, k)));

        var taxUnit = TargetTaxUnit ?? TaxUnit.Invoice;

        MasterResults.Clear();
        foreach (var product in filtered)
        {
            MasterResults.Add(ProductMasterSearchItem.FromEntity(product, taxUnit, unitPriceCalculator));
        }

        MasterSelectedIndex = MasterResults.Count > 0 ? 0 : -1;
    }

    private void ApplyHistoryFilter()
    {
        var keywords = SplitKeywords(HistorySearchKeyword);

        // 売上テーブルにカナ列がないため、履歴軸はカナ検索の対象外（docs/design_document.md 参照）。
        var filtered = keywords.Length == 0
            ? _historyHits
            : _historyHits.Where(h => keywords.All(k =>
                Contains(h.ProductCode, k)
                || Contains(h.ProductName, k)
                || Contains(h.Specification, k)));

        HistoryResults.Clear();
        foreach (var hit in filtered)
        {
            HistoryResults.Add(ProductHistorySearchItem.FromHit(hit));
        }

        HistorySelectedIndex = HistoryResults.Count > 0 ? 0 : -1;
    }

    private static string[] SplitKeywords(string keyword) =>
        keyword.Split(' ', '　').Where(k => !string.IsNullOrWhiteSpace(k)).ToArray();

    private static bool Contains(string? source, string keyword) =>
        source is not null && source.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>「マスタから」の選択行をカゴへ積む（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private void AddMasterToBasket(object? item)
    {
        if (item is not ProductMasterSearchItem searchItem)
        {
            return;
        }

        AddToBasket(new ProductSelection(
            searchItem.ProductCode,
            searchItem.ProductName,
            searchItem.Specification,
            searchItem.UnitName,
            searchItem.UnitPrice,
            searchItem.CostPrice,
            searchItem.TaxCategory,
            ProductSelectionSource.Master));
    }

    /// <summary>「過去の取引履歴から」の選択行をカゴへ積む（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private void AddHistoryToBasket(object? item)
    {
        if (item is not ProductHistorySearchItem searchItem)
        {
            return;
        }

        // 原価は履歴軸でも商品マスタの標準原価を転記する（暫定。docs/database-schema.md 参照）。
        // 過去の売上行が保持する原価は当時のスナップショットであり、粗利計算には現在の標準原価を使う方針のため。
        var costPrice = _allProducts.FirstOrDefault(p => p.ProductCode == searchItem.ProductCode)?.StandardCostPrice ?? 0m;

        AddToBasket(new ProductSelection(
            searchItem.ProductCode,
            searchItem.ProductName,
            searchItem.Specification,
            searchItem.UnitName,
            searchItem.UnitPrice,
            costPrice,
            searchItem.TaxCategory,
            ProductSelectionSource.History));
    }

    private void AddToBasket(ProductSelection selection)
    {
        if (Basket.Any(b => b.ProductCode == selection.ProductCode))
        {
            StatusMessage = $"{selection.ProductCode} は既に選択されています。";
            return;
        }

        if (Basket.Count >= MaxBasketSize)
        {
            MessageBox.Show($"一括転記は最大{MaxBasketSize}件までです。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Basket.Add(selection);
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void RemoveFromBasket(object? item)
    {
        if (item is ProductSelection selection)
        {
            Basket.Remove(selection);
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        if (Basket.Count == 0)
        {
            return;
        }

        CloseWith(Basket.ToList());
    }
}
