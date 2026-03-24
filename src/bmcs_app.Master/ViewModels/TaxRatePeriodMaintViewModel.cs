using System.Collections.ObjectModel;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Master.ViewModels;

public class TaxRatePeriodMaintViewModel : BindableBase
{
    private readonly ITaxRatePeriodRepository _repo;

    private const int PageSize = 100;

    private List<TaxRatePeriod> _allPeriods      = new();
    private List<TaxRatePeriod> _filteredPeriods = new();

    public ObservableCollection<TaxRatePeriod> Periods { get; } = new();

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

    public int TotalPages => _filteredPeriods.Count == 0
        ? 1
        : (int)Math.Ceiling(_filteredPeriods.Count / (double)PageSize);

    public string PageLabel  => $"{CurrentPage} / {TotalPages} ページ";
    public string RangeLabel => _filteredPeriods.Count == 0
        ? "0 件"
        : $"{(CurrentPage - 1) * PageSize + 1}〜{Math.Min(CurrentPage * PageSize, _filteredPeriods.Count)} 件 / 全 {_filteredPeriods.Count} 件";

    public DelegateCommand FirstPageCommand { get; }
    public DelegateCommand PrevPageCommand  { get; }
    public DelegateCommand NextPageCommand  { get; }
    public DelegateCommand LastPageCommand  { get; }

    // ── 選択・編集 ────────────────────────────────────────
    private TaxRatePeriod? _selectedPeriod;
    public TaxRatePeriod? SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (SetProperty(ref _selectedPeriod, value) && value is not null)
                LoadToForm(value);
        }
    }

    private int? _editingId;

    private DateTime? _editStartDate;
    public DateTime? EditStartDate
    {
        get => _editStartDate;
        set => SetProperty(ref _editStartDate, value);
    }

    private DateTime? _editEndDate;
    public DateTime? EditEndDate
    {
        get => _editEndDate;
        set => SetProperty(ref _editEndDate, value);
    }

    private string _editPrimaryTaxRate = "";
    public string EditPrimaryTaxRate
    {
        get => _editPrimaryTaxRate;
        set => SetProperty(ref _editPrimaryTaxRate, value);
    }

    private string _editSecondaryTaxRate = "";
    public string EditSecondaryTaxRate
    {
        get => _editSecondaryTaxRate;
        set => SetProperty(ref _editSecondaryTaxRate, value);
    }

    private string _editTertiaryTaxRate = "";
    public string EditTertiaryTaxRate
    {
        get => _editTertiaryTaxRate;
        set => SetProperty(ref _editTertiaryTaxRate, value);
    }

    private string _statusMessage = "準備完了";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public DelegateCommand NewCommand    { get; }
    public DelegateCommand SaveCommand   { get; }
    public DelegateCommand DeleteCommand { get; }

    // ── コンストラクタ ─────────────────────────────────────
    public TaxRatePeriodMaintViewModel(ITaxRatePeriodRepository repo)
    {
        _repo = repo;

        NewCommand    = new DelegateCommand(OnNew);
        SaveCommand   = new DelegateCommand(async () => await OnSaveAsync());
        DeleteCommand = new DelegateCommand(async () => await OnDeleteAsync(), CanDelete)
                            .ObservesProperty(() => SelectedPeriod);
        SearchCommand = new DelegateCommand(() => { CurrentPage = 1; ApplyFilter(); });

        FirstPageCommand = new DelegateCommand(() => GoToPage(1),              () => CurrentPage > 1)
                               .ObservesProperty(() => CurrentPage);
        PrevPageCommand  = new DelegateCommand(() => GoToPage(CurrentPage - 1),() => CurrentPage > 1)
                               .ObservesProperty(() => CurrentPage);
        NextPageCommand  = new DelegateCommand(() => GoToPage(CurrentPage + 1),() => CurrentPage < TotalPages)
                               .ObservesProperty(() => CurrentPage);
        LastPageCommand  = new DelegateCommand(() => GoToPage(TotalPages),     () => CurrentPage < TotalPages)
                               .ObservesProperty(() => CurrentPage);

        _ = LoadAsync();
    }

    // ── データ読み込み ─────────────────────────────────────
    private async Task LoadAsync()
    {
        try
        {
            _allPeriods = (await _repo.GetAllAsync()).ToList();
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
        _filteredPeriods = string.IsNullOrEmpty(kw)
            ? _allPeriods
            : _allPeriods.Where(p =>
                p.StartDate.ToString("yyyy/MM/dd").Contains(kw) ||
                (p.EndDate?.ToString("yyyy/MM/dd") ?? "").Contains(kw) ||
                p.PrimaryTaxRate.ToString().Contains(kw) ||
                p.SecondaryTaxRate.ToString().Contains(kw))
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
        var page = _filteredPeriods
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        Periods.Clear();
        foreach (var item in page)
            Periods.Add(item);

        RaisePropertyChanged(nameof(PageLabel));
        RaisePropertyChanged(nameof(RangeLabel));
        StatusMessage = RangeLabel;
    }

    // ── フォーム操作 ───────────────────────────────────────
    private void OnNew()
    {
        _editingId           = null;
        EditStartDate        = null;
        EditEndDate          = null;
        EditPrimaryTaxRate   = "";
        EditSecondaryTaxRate = "";
        EditTertiaryTaxRate  = "";
        _selectedPeriod      = null;
        RaisePropertyChanged(nameof(SelectedPeriod));
        StatusMessage = "新規入力";
    }

    private void LoadToForm(TaxRatePeriod p)
    {
        _editingId           = p.TaxRatePeriodId;
        EditStartDate        = p.StartDate.ToDateTime(TimeOnly.MinValue);
        EditEndDate          = p.EndDate?.ToDateTime(TimeOnly.MinValue);
        EditPrimaryTaxRate   = (p.PrimaryTaxRate   * 100).ToString("0.##");
        EditSecondaryTaxRate = (p.SecondaryTaxRate * 100).ToString("0.##");
        EditTertiaryTaxRate  = p.TertiaryTaxRate.HasValue ? (p.TertiaryTaxRate.Value * 100).ToString("0.##") : "";
        StatusMessage        = $"編集中: {p.StartDate:yyyy/MM/dd} 〜";
    }

    private async Task OnSaveAsync()
    {
        if (EditStartDate is null)
        {
            StatusMessage = "適用開始日を入力してください";
            return;
        }
        var startDate = DateOnly.FromDateTime(EditStartDate.Value);

        DateOnly? endDate = null;
        if (EditEndDate is not null)
        {
            var ed = DateOnly.FromDateTime(EditEndDate.Value);
            if (ed < startDate)
            {
                StatusMessage = "適用終了日は開始日以降を指定してください";
                return;
            }
            endDate = ed;
        }

        if (!decimal.TryParse(EditPrimaryTaxRate.Trim(), out var primaryPct) || primaryPct < 0)
        {
            StatusMessage = "標準税率は 0 以上の数値を入力してください";
            return;
        }

        if (!decimal.TryParse(EditSecondaryTaxRate.Trim(), out var secondaryPct) || secondaryPct < 0)
        {
            StatusMessage = "軽減税率は 0 以上の数値を入力してください";
            return;
        }

        decimal? tertiary = null;
        if (!string.IsNullOrWhiteSpace(EditTertiaryTaxRate))
        {
            if (!decimal.TryParse(EditTertiaryTaxRate.Trim(), out var t) || t < 0)
            {
                StatusMessage = "第3税率は 0 以上の数値を入力してください";
                return;
            }
            tertiary = t / 100;
        }

        try
        {
            await _repo.UpsertAsync(_editingId, startDate, endDate, primaryPct / 100, secondaryPct / 100, tertiary);
            StatusMessage = _editingId is null ? "登録しました" : "更新しました";
            await LoadAsync();
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
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private bool CanDelete() => SelectedPeriod is not null;
}
