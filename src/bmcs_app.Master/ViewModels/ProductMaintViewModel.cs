using System.Collections.ObjectModel;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using bmcs_app.Infrastructure.Repositories;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Master.ViewModels;

public class ProductMaintViewModel : BindableBase
{
    private readonly IProductRepository _repo;
    private readonly TaxTypeRepository  _taxTypeRepo;

    private const int PageSize = 100;

    private List<Product> _allProducts      = new();
    private List<Product> _filteredProducts = new();

    public ObservableCollection<Product>               Products { get; } = new();
    public ObservableCollection<TaxTypeClassification> TaxTypes { get; } = new();

    public record TaxRateTypeOption(byte Value, string Label);
    public List<TaxRateTypeOption> TaxRateTypeOptions { get; } = new()
    {
        new(1, "標準税率"),
        new(2, "軽減税率"),
        new(3, "特殊税率"),
    };

    // ── 検索 ──────────────────────────────────────────────
    private string _searchKeyword = "";
    public string SearchKeyword
    {
        get => _searchKeyword;
        set => SetProperty(ref _searchKeyword, value);
    }

    public DelegateCommand SearchCommand { get; }

    // ── ページネーション ───────────────────────────────────
    private int _currentPage = 1;
    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                RaisePropertyChanged(nameof(PageLabel));
                RaisePropertyChanged(nameof(RangeLabel));
            }
        }
    }

    public int TotalPages => _filteredProducts.Count == 0
        ? 1
        : (int)Math.Ceiling(_filteredProducts.Count / (double)PageSize);

    public string PageLabel  => $"{CurrentPage} / {TotalPages} ページ";
    public string RangeLabel => _filteredProducts.Count == 0
        ? "0 件"
        : $"{(CurrentPage - 1) * PageSize + 1}〜{Math.Min(CurrentPage * PageSize, _filteredProducts.Count)} 件 / 全 {_filteredProducts.Count} 件";

    public DelegateCommand FirstPageCommand { get; }
    public DelegateCommand PrevPageCommand  { get; }
    public DelegateCommand NextPageCommand  { get; }
    public DelegateCommand LastPageCommand  { get; }

    // ── 選択・編集 ────────────────────────────────────────
    private Product? _selectedProduct;
    public Product? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (SetProperty(ref _selectedProduct, value) && value is not null)
                LoadToForm(value);
        }
    }

    private int? _editingId;

    private string _editCode = "";
    public string EditCode
    {
        get => _editCode;
        set => SetProperty(ref _editCode, value);
    }

    private string _editName = "";
    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    private TaxTypeClassification? _editTaxType;
    public TaxTypeClassification? EditTaxType
    {
        get => _editTaxType;
        set => SetProperty(ref _editTaxType, value);
    }

    private TaxRateTypeOption? _editTaxRateType;
    public TaxRateTypeOption? EditTaxRateType
    {
        get => _editTaxRateType;
        set => SetProperty(ref _editTaxRateType, value);
    }

    private decimal _editCostPrice;
    public decimal EditCostPrice
    {
        get => _editCostPrice;
        set => SetProperty(ref _editCostPrice, value);
    }

    private bool _editIsMiscellaneous = false;
    public bool EditIsMiscellaneous
    {
        get => _editIsMiscellaneous;
        set
        {
            if (SetProperty(ref _editIsMiscellaneous, value))
                IsEditCodeReadOnly = value && _editingId is not null;
        }
    }

    private bool _isEditCodeReadOnly = false;
    public bool IsEditCodeReadOnly
    {
        get => _isEditCodeReadOnly;
        private set => SetProperty(ref _isEditCodeReadOnly, value);
    }

    // ── ステータス ────────────────────────────────────────
    private string _statusMessage = "準備完了";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // ── コマンド ──────────────────────────────────────────
    public DelegateCommand NewCommand    { get; }
    public DelegateCommand SaveCommand   { get; }
    public DelegateCommand DeleteCommand { get; }

    // ── コンストラクタ ─────────────────────────────────────
    public ProductMaintViewModel(IProductRepository repo, TaxTypeRepository taxTypeRepo)
    {
        _repo        = repo;
        _taxTypeRepo = taxTypeRepo;

        NewCommand    = new DelegateCommand(OnNew);
        SaveCommand   = new DelegateCommand(async () => await OnSaveAsync());
        DeleteCommand = new DelegateCommand(async () => await OnDeleteAsync(), CanDelete)
                            .ObservesProperty(() => SelectedProduct);
        SearchCommand = new DelegateCommand(() => { CurrentPage = 1; ApplyFilter(); });

        FirstPageCommand = new DelegateCommand(() => GoToPage(1),               () => CurrentPage > 1)
                               .ObservesProperty(() => CurrentPage);
        PrevPageCommand  = new DelegateCommand(() => GoToPage(CurrentPage - 1), () => CurrentPage > 1)
                               .ObservesProperty(() => CurrentPage);
        NextPageCommand  = new DelegateCommand(() => GoToPage(CurrentPage + 1), () => CurrentPage < TotalPages)
                               .ObservesProperty(() => CurrentPage);
        LastPageCommand  = new DelegateCommand(() => GoToPage(TotalPages),      () => CurrentPage < TotalPages)
                               .ObservesProperty(() => CurrentPage);

        _ = LoadAsync();
    }

    // ── データ読み込み ─────────────────────────────────────
    private async Task LoadAsync()
    {
        try
        {
            var types = await _taxTypeRepo.GetAllAsync();
            TaxTypes.Clear();
            foreach (var t in types)
                TaxTypes.Add(t);
        }
        catch (Exception ex)
        {
            StatusMessage = $"税種別読み込みエラー: {ex.Message}";
        }

        await LoadProductsAsync();
    }

    private async Task LoadProductsAsync()
    {
        try
        {
            _allProducts = (await _repo.GetAllAsync()).ToList();
            CurrentPage = 1;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = $"読み込みエラー: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var kw = SearchKeyword.Trim();
        _filteredProducts = string.IsNullOrEmpty(kw)
            ? _allProducts
            : _allProducts.Where(p =>
                p.ProductCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                p.ProductName.Contains(kw, StringComparison.OrdinalIgnoreCase))
              .ToList();

        RaisePropertyChanged(nameof(TotalPages));
        ApplyPage();
    }

    private void GoToPage(int page)
    {
        CurrentPage = Math.Clamp(page, 1, TotalPages);
        ApplyPage();
    }

    private void ApplyPage()
    {
        var page = _filteredProducts
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        Products.Clear();
        foreach (var item in page)
            Products.Add(item);

        RaisePropertyChanged(nameof(PageLabel));
        RaisePropertyChanged(nameof(RangeLabel));
        StatusMessage = RangeLabel;
    }

    // ── フォーム操作 ───────────────────────────────────────
    private void OnNew()
    {
        _editingId           = null;
        EditCode             = "";
        EditName             = "";
        EditTaxType          = TaxTypes.FirstOrDefault();
        EditTaxRateType      = TaxRateTypeOptions.FirstOrDefault();
        EditCostPrice        = 0;
        EditIsMiscellaneous  = false;
        IsEditCodeReadOnly   = false;
        _selectedProduct     = null;
        RaisePropertyChanged(nameof(SelectedProduct));
        StatusMessage = "新規入力";
    }

    private void LoadToForm(Product p)
    {
        _editingId          = p.ProductId;
        EditCode            = p.ProductCode;
        EditName            = p.ProductName;
        EditTaxType         = TaxTypes.FirstOrDefault(t => t.TaxTypeId == p.TaxTypeId);
        EditTaxRateType     = TaxRateTypeOptions.FirstOrDefault(o => o.Value == p.TaxRateType);
        EditCostPrice       = p.CostPrice;
        EditIsMiscellaneous = p.IsMiscellaneous;
        IsEditCodeReadOnly  = p.IsMiscellaneous;
        StatusMessage       = $"編集中: {p.ProductName}";
    }

    private async Task OnSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditCode) || string.IsNullOrWhiteSpace(EditName))
        {
            StatusMessage = "コードと名称は必須です";
            return;
        }
        if (EditTaxType is null || EditTaxRateType is null)
        {
            StatusMessage = "税種別と税率区分は必須です";
            return;
        }
        try
        {
            await _repo.UpsertAsync(
                _editingId,
                EditCode.Trim(),
                EditName.Trim(),
                EditTaxType.TaxTypeId,
                EditTaxRateType.Value,
                EditCostPrice,
                EditIsMiscellaneous);
            StatusMessage = _editingId is null ? "登録しました" : "更新しました";
            await LoadProductsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private async Task OnDeleteAsync()
    {
        if (_editingId is null) return;
        try
        {
            await _repo.DeleteAsync(_editingId.Value);
            OnNew();
            StatusMessage = "削除しました";
            await LoadProductsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private bool CanDelete() => SelectedProduct is not null && !SelectedProduct.IsMiscellaneous;
}
