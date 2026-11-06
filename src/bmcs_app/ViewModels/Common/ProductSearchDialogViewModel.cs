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
    UnitPriceService unitPriceService,
    ProductSupplierCostService productSupplierCostService,
    SupplierService supplierService)
    : DialogViewModelBase<IReadOnlyList<ProductSelection>>
{
    public const int MaxBasketSize = 6;

    private List<Product> _allProducts = [];
    private List<SelectableSupplierCost> _supplierCosts = [];
    private Dictionary<string, string> _supplierNames = [];
    private List<ProductHistoryHit> _historyHits = [];
    private bool _historyLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomer))]
    [NotifyPropertyChangedFor(nameof(ShowCustomerRequiredNotice))]
    public partial string? TargetCustomerCode { get; set; }

    /// <summary>
    /// 対象得意先。マスタ軸の単価（得意先別単価・定価掛率・外税／内税）を決める。
    /// 呼び出し元が <see cref="TargetCustomerCode"/> と一緒に設定する。
    /// 未設定（得意先未選択）のときは商品マスタの外税単価を使う（請求単位・伝票単位が多数派のため）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MasterUnitPriceHeader))]
    public partial Customer? TargetCustomer { get; set; }

    /// <summary>伝票日付。内税得意先の定価掛率計算で使う税率の適用日。</summary>
    public DateOnly TargetSlipDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>マスタ軸の単価列ヘッダ。表示と転記される単価が食い違わないよう、転記元と同じ判定を使う。</summary>
    public string MasterUnitPriceHeader => TargetCustomer?.TaxUnit == TaxUnit.Line ? "単価(税込)" : "単価(税抜)";

    private UnitPriceResolver? _unitPriceResolver;

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
        _supplierCosts = await productSupplierCostService.GetSelectableCostsAsync();
        _unitPriceResolver = await unitPriceService.CreateResolverAsync(TargetCustomer, TargetSlipDate);
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
        _supplierNames = (await supplierService.GetSuppliersAsync()).ToDictionary(s => s.SupplierCode, s => s.SupplierName);
        _historyLoaded = true;
        ApplyHistoryFilter();
    });

    partial void OnMasterSearchKeywordChanged(string value) => ApplyMasterFilter();

    partial void OnHistorySearchKeywordChanged(string value) => ApplyHistoryFilter();

    private void ApplyMasterFilter()
    {
        var keywords = SplitKeywords(MasterSearchKeyword);

        // 商品マスタに仕入先別原価を LEFT JOIN した行を、商品→仕入先の順に並べる（_allProducts・_supplierCosts は取得時にコード順）。
        // 原価がある商品は仕入先ごとの行のみ、無い商品は仕入先なし（標準原価）の1行。
        var costsByProduct = _supplierCosts.ToLookup(c => c.ProductCode);
        var rows = _allProducts.SelectMany(product =>
        {
            var costs = costsByProduct[product.ProductCode].ToList();
            return costs.Count == 0
                ? [(Product: product, Cost: (SelectableSupplierCost?)null)]
                : costs.Select(c => (Product: product, Cost: (SelectableSupplierCost?)c));
        });

        var filtered = keywords.Length == 0
            ? rows
            : rows.Where(r => keywords.All(k =>
                Contains(r.Product.ProductCode, k)
                || Contains(r.Product.ProductName, k)
                || Contains(r.Product.JanCode, k)
                || Contains(r.Product.ProductNameKana, k)
                || Contains(r.Product.Specification, k)
                || Contains(r.Cost?.SupplierCode, k)
                || Contains(r.Cost?.SupplierName, k)));

        MasterResults.Clear();
        foreach (var (product, cost) in filtered)
        {
            MasterResults.Add(ProductMasterSearchItem.FromEntity(product, _unitPriceResolver!.Resolve(product), cost));
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
                || Contains(h.Specification, k)
                || Contains(h.SupplierCode, k)
                || Contains(h.SupplierCode is null ? null : _supplierNames.GetValueOrDefault(h.SupplierCode), k)));

        HistoryResults.Clear();
        foreach (var hit in filtered)
        {
            HistoryResults.Add(ToHistoryItem(hit));
        }

        HistorySelectedIndex = HistoryResults.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// 履歴行を一覧行にする。仕入先は前回の売上行の仕入先を引き継ぎ、原価は現在の仕入先別原価から決める。
    /// 前回の仕入先に、いま有効な原価（有効な仕入先の有効な原価行）が無ければ、仕入先なしにして標準原価にする。
    /// </summary>
    private ProductHistorySearchItem ToHistoryItem(ProductHistoryHit hit)
    {
        var supplierCost = hit.SupplierCode is null
            ? null
            : _supplierCosts.FirstOrDefault(c => c.ProductCode == hit.ProductCode && c.SupplierCode == hit.SupplierCode);
        var costPrice = supplierCost?.CostPrice
            ?? _allProducts.FirstOrDefault(p => p.ProductCode == hit.ProductCode)?.StandardCostPrice
            ?? 0m;

        return new ProductHistorySearchItem(
            hit.ProductCode,
            hit.ProductName,
            hit.Specification,
            hit.UnitName,
            hit.LastUnitPrice,
            supplierCost?.SupplierCode,
            supplierCost?.SupplierName,
            costPrice,
            hit.LastSlipDate.ToString("yyyy/MM/dd"),
            hit.TaxCategory,
            ProductMasterSearchItem.TaxCategoryDisplayOf(hit.TaxCategory));
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
            searchItem.SupplierCode,
            searchItem.SupplierName,
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

        AddToBasket(new ProductSelection(
            searchItem.ProductCode,
            searchItem.ProductName,
            searchItem.Specification,
            searchItem.UnitName,
            searchItem.UnitPrice,
            searchItem.SupplierCode,
            searchItem.SupplierName,
            searchItem.CostPrice,
            searchItem.TaxCategory,
            ProductSelectionSource.History));
    }

    private void AddToBasket(ProductSelection selection)
    {
        // 同じ商品でも仕入先が違えば別の行として選べる（仕入先ごとに原価が違うため）。
        if (Basket.Any(b => b.ProductCode == selection.ProductCode && b.SupplierCode == selection.SupplierCode))
        {
            StatusMessage = selection.SupplierCode is null
                ? $"{selection.ProductCode} は既に選択されています。"
                : $"{selection.ProductCode}（仕入先 {selection.SupplierCode}）は既に選択されています。";
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
