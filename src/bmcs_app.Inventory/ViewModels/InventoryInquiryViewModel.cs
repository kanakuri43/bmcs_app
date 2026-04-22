using System.Collections.ObjectModel;
using bmcs_app.Core.Interfaces;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Inventory.ViewModels;

public class InventoryCurrentRow
{
    public int      ProductId           { get; init; }
    public string   ProductCode         { get; init; } = string.Empty;
    public string   ProductName         { get; init; } = string.Empty;
    public decimal? LastCountQty        { get; init; }
    public DateOnly? LastCountDate      { get; init; }
    public decimal  PurchaseQty         { get; init; }
    public decimal  SaleQty             { get; init; }
    public decimal? CurrentStock        { get; init; }

    public bool   IsNeverCounted       => LastCountDate is null;
    public bool   HasNoStockData       => CurrentStock is null;
    public string LastCountDateDisplay => LastCountDate?.ToString("yyyy/MM/dd") ?? string.Empty;
    public string LastCountQtyDisplay  => LastCountQty.HasValue ? LastCountQty.Value.ToString("N2") : string.Empty;
    public string CurrentStockDisplay  => CurrentStock.HasValue ? CurrentStock.Value.ToString("N2") : "未棚卸";
}

public class InventoryInquiryViewModel : BindableBase
{
    private readonly IInventoryCurrentRepository _repo;

    private List<InventoryCurrentRow>            _allItems      = new();
    private List<InventoryCurrentRow>            _filteredItems = new();

    public ObservableCollection<InventoryCurrentRow> Items { get; } = new();

    // ── 検索 ────────────────────────────────────────────────
    private string _searchKeyword = string.Empty;
    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            SetProperty(ref _searchKeyword, value);
            _currentPage = 1;
            ApplyFilter();
        }
    }

    // ── ページネーション ────────────────────────────────────
    private const int PageSize = 100;

    private int _currentPage = 1;
    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            SetProperty(ref _currentPage, value);
            RaisePropertyChanged(nameof(PageLabel));
            RaisePropertyChanged(nameof(RangeLabel));
        }
    }

    public int    TotalPages => _filteredItems.Count == 0 ? 1 : (int)Math.Ceiling(_filteredItems.Count / (double)PageSize);
    public string PageLabel  => $"{CurrentPage} / {TotalPages} ページ";
    public string RangeLabel
    {
        get
        {
            if (_filteredItems.Count == 0) return "0 件";
            var from = (_currentPage - 1) * PageSize + 1;
            var to   = Math.Min(_currentPage * PageSize, _filteredItems.Count);
            return $"{from}〜{to} 件 / 全 {_filteredItems.Count} 件";
        }
    }

    // ── ステータス ──────────────────────────────────────────
    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // ── コマンド ────────────────────────────────────────────
    public DelegateCommand SearchCommand  { get; }
    public DelegateCommand RefreshCommand { get; }
    public DelegateCommand FirstPageCommand     { get; }
    public DelegateCommand PrevPageCommand      { get; }
    public DelegateCommand NextPageCommand      { get; }
    public DelegateCommand LastPageCommand      { get; }

    public InventoryInquiryViewModel(IInventoryCurrentRepository repo)
    {
        _repo = repo;

        SearchCommand  = new DelegateCommand(() => { _currentPage = 1; ApplyFilter(); });
        RefreshCommand = new DelegateCommand(async () => await LoadAsync());
        FirstPageCommand     = new DelegateCommand(() => { CurrentPage = 1; ApplyPage(); },
                                   () => CurrentPage > 1).ObservesProperty(() => CurrentPage);
        PrevPageCommand      = new DelegateCommand(() => { CurrentPage--; ApplyPage(); },
                                   () => CurrentPage > 1).ObservesProperty(() => CurrentPage);
        NextPageCommand      = new DelegateCommand(() => { CurrentPage++; ApplyPage(); },
                                   () => CurrentPage < TotalPages).ObservesProperty(() => CurrentPage);
        LastPageCommand      = new DelegateCommand(() => { CurrentPage = TotalPages; ApplyPage(); },
                                   () => CurrentPage < TotalPages).ObservesProperty(() => CurrentPage);

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            StatusMessage = "読み込み中...";
            var stocks = await _repo.GetAllAsync();
            _allItems = stocks.Select(s => new InventoryCurrentRow
            {
                ProductId     = s.ProductId,
                ProductCode   = s.ProductCode,
                ProductName   = s.ProductName,
                LastCountDate = s.LastCountDate,
                LastCountQty  = s.LastCountQty,
                PurchaseQty   = s.PurchaseQty,
                SaleQty       = s.SaleQty,
                CurrentStock  = s.CurrentStock,
            }).ToList();
            _currentPage = 1;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = $"読み込みエラー: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var keyword = SearchKeyword.Trim();
        _filteredItems = string.IsNullOrEmpty(keyword)
            ? _allItems.ToList()
            : _allItems.Where(r =>
                r.ProductCode.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.ProductName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
              .ToList();

        RaisePropertyChanged(nameof(TotalPages));
        RaisePropertyChanged(nameof(PageLabel));
        RaisePropertyChanged(nameof(RangeLabel));
        NextPageCommand.RaiseCanExecuteChanged();
        LastPageCommand.RaiseCanExecuteChanged();
        ApplyPage();
    }

    private void ApplyPage()
    {
        var slice = _filteredItems
            .Skip((_currentPage - 1) * PageSize)
            .Take(PageSize);
        Items.Clear();
        foreach (var r in slice) Items.Add(r);
        RaisePropertyChanged(nameof(PageLabel));
        RaisePropertyChanged(nameof(RangeLabel));
        StatusMessage = RangeLabel;
    }
}
