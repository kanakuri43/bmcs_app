using System.Collections.ObjectModel;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Master.ViewModels;

public class EmployeeMaintViewModel : BindableBase
{
    private readonly IEmployeeRepository _repo;

    private const int PageSize = 100;

    private List<Employee> _allEmployees      = new();
    private List<Employee> _filteredEmployees = new();

    public ObservableCollection<Employee> Employees { get; } = new();

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

    public int TotalPages => _filteredEmployees.Count == 0
        ? 1
        : (int)Math.Ceiling(_filteredEmployees.Count / (double)PageSize);

    public string PageLabel  => $"{CurrentPage} / {TotalPages} ページ";
    public string RangeLabel => _filteredEmployees.Count == 0
        ? "0 件"
        : $"{(CurrentPage - 1) * PageSize + 1}〜{Math.Min(CurrentPage * PageSize, _filteredEmployees.Count)} 件 / 全 {_filteredEmployees.Count} 件";

    public DelegateCommand FirstPageCommand { get; }
    public DelegateCommand PrevPageCommand  { get; }
    public DelegateCommand NextPageCommand  { get; }
    public DelegateCommand LastPageCommand  { get; }

    // ── 選択・編集 ────────────────────────────────────────
    private Employee? _selectedEmployee;
    public Employee? SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (SetProperty(ref _selectedEmployee, value) && value is not null)
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
    public EmployeeMaintViewModel(IEmployeeRepository repo)
    {
        _repo = repo;

        NewCommand    = new DelegateCommand(OnNew);
        SaveCommand   = new DelegateCommand(async () => await OnSaveAsync());
        DeleteCommand = new DelegateCommand(async () => await OnDeleteAsync(), CanDelete)
                            .ObservesProperty(() => SelectedEmployee);
        SearchCommand = new DelegateCommand(() => { CurrentPage = 1; ApplyFilter(); });

        FirstPageCommand = new DelegateCommand(() => GoToPage(1),             () => CurrentPage > 1)
                               .ObservesProperty(() => CurrentPage);
        PrevPageCommand  = new DelegateCommand(() => GoToPage(CurrentPage - 1), () => CurrentPage > 1)
                               .ObservesProperty(() => CurrentPage);
        NextPageCommand  = new DelegateCommand(() => GoToPage(CurrentPage + 1), () => CurrentPage < TotalPages)
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
            _allEmployees = (await _repo.GetAllAsync()).ToList();
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
        _filteredEmployees = string.IsNullOrEmpty(kw)
            ? _allEmployees
            : _allEmployees.Where(e =>
                e.EmployeeCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                e.EmployeeName.Contains(kw, StringComparison.OrdinalIgnoreCase))
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
        var page = _filteredEmployees
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        Employees.Clear();
        foreach (var item in page)
            Employees.Add(item);

        RaisePropertyChanged(nameof(PageLabel));
        RaisePropertyChanged(nameof(RangeLabel));
        StatusMessage = RangeLabel;
    }

    // ── フォーム操作 ───────────────────────────────────────
    private void OnNew()
    {
        _editingId = null;
        EditCode = "";
        EditName = "";
        _selectedEmployee = null;
        RaisePropertyChanged(nameof(SelectedEmployee));
        StatusMessage = "新規入力";
    }

    private void LoadToForm(Employee emp)
    {
        _editingId = emp.EmployeeId;
        EditCode = emp.EmployeeCode;
        EditName = emp.EmployeeName;
        StatusMessage = $"編集中: {emp.EmployeeName}";
    }

    private async Task OnSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditCode) || string.IsNullOrWhiteSpace(EditName))
        {
            StatusMessage = "コードと名称は必須です";
            return;
        }
        try
        {
            await _repo.UpsertAsync(_editingId, EditCode.Trim(), EditName.Trim());
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

    private bool CanDelete() => SelectedEmployee is not null;
}
