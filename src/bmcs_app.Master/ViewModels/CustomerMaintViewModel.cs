using System.Collections.ObjectModel;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Master.ViewModels;

public class CustomerMaintViewModel : BindableBase
{
    private readonly ICustomerRepository _repo;

    private const int PageSize = 100;

    private List<Customer> _allCustomers      = new();
    private List<Customer> _filteredCustomers = new();

    public ObservableCollection<Customer>                  Customers    { get; } = new();
    public ObservableCollection<TaxFractionClassification> TaxFractions { get; } = new();
    public ObservableCollection<TaxCalcUnitClassification> TaxCalcUnits { get; } = new();
    public ObservableCollection<Employee?>                 Employees    { get; } = new();

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

    public int TotalPages => _filteredCustomers.Count == 0
        ? 1
        : (int)Math.Ceiling(_filteredCustomers.Count / (double)PageSize);

    public string PageLabel  => $"{CurrentPage} / {TotalPages} ページ";
    public string RangeLabel => _filteredCustomers.Count == 0
        ? "0 件"
        : $"{(CurrentPage - 1) * PageSize + 1}〜{Math.Min(CurrentPage * PageSize, _filteredCustomers.Count)} 件 / 全 {_filteredCustomers.Count} 件";

    public DelegateCommand FirstPageCommand { get; }
    public DelegateCommand PrevPageCommand  { get; }
    public DelegateCommand NextPageCommand  { get; }
    public DelegateCommand LastPageCommand  { get; }

    // ── 選択・編集 ────────────────────────────────────────
    private Customer? _selectedCustomer;
    public Customer? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value) && value is not null)
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

    private string _editClosingDay = "";
    public string EditClosingDay
    {
        get => _editClosingDay;
        set => SetProperty(ref _editClosingDay, value);
    }

    private TaxFractionClassification? _editTaxFraction;
    public TaxFractionClassification? EditTaxFraction
    {
        get => _editTaxFraction;
        set => SetProperty(ref _editTaxFraction, value);
    }

    private TaxCalcUnitClassification? _editTaxCalcUnit;
    public TaxCalcUnitClassification? EditTaxCalcUnit
    {
        get => _editTaxCalcUnit;
        set => SetProperty(ref _editTaxCalcUnit, value);
    }

    private Employee? _editEmployee;
    public Employee? EditEmployee
    {
        get => _editEmployee;
        set => SetProperty(ref _editEmployee, value);
    }

    private string _editPostalCode = "";
    public string EditPostalCode
    {
        get => _editPostalCode;
        set => SetProperty(ref _editPostalCode, value);
    }

    private string _editAddress1 = "";
    public string EditAddress1
    {
        get => _editAddress1;
        set => SetProperty(ref _editAddress1, value);
    }

    private string _editAddress2 = "";
    public string EditAddress2
    {
        get => _editAddress2;
        set => SetProperty(ref _editAddress2, value);
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
    public CustomerMaintViewModel(ICustomerRepository repo)
    {
        _repo = repo;

        NewCommand    = new DelegateCommand(OnNew);
        SaveCommand   = new DelegateCommand(async () => await OnSaveAsync());
        DeleteCommand = new DelegateCommand(async () => await OnDeleteAsync(), CanDelete)
                            .ObservesProperty(() => SelectedCustomer);
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
            var customers    = (await _repo.GetAllAsync()).ToList();
            var taxFractions = (await _repo.GetTaxFractionsAsync()).ToList();
            var taxCalcUnits = (await _repo.GetTaxCalcUnitsAsync()).ToList();
            var employees    = (await _repo.GetEmployeesAsync()).ToList();

            TaxFractions.Clear();
            foreach (var f in taxFractions) TaxFractions.Add(f);

            TaxCalcUnits.Clear();
            foreach (var u in taxCalcUnits) TaxCalcUnits.Add(u);

            Employees.Clear();
            Employees.Add(null);  // 担当者なし
            foreach (var e in employees) Employees.Add(e);

            _allCustomers = customers;
            CurrentPage   = 1;
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
        _filteredCustomers = string.IsNullOrEmpty(kw)
            ? _allCustomers
            : _allCustomers.Where(c =>
                c.CustomerCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                c.CustomerName.Contains(kw, StringComparison.OrdinalIgnoreCase))
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
        var page = _filteredCustomers
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        Customers.Clear();
        foreach (var item in page)
            Customers.Add(item);

        RaisePropertyChanged(nameof(PageLabel));
        RaisePropertyChanged(nameof(RangeLabel));
        StatusMessage = RangeLabel;
    }

    // ── フォーム操作 ───────────────────────────────────────
    private void OnNew()
    {
        _editingId       = null;
        EditCode         = "";
        EditName         = "";
        EditClosingDay   = "";
        EditTaxFraction  = TaxFractions.FirstOrDefault();
        EditTaxCalcUnit  = TaxCalcUnits.FirstOrDefault();
        EditEmployee     = null;
        EditPostalCode   = "";
        EditAddress1     = "";
        EditAddress2     = "";
        _selectedCustomer = null;
        RaisePropertyChanged(nameof(SelectedCustomer));
        StatusMessage = "新規入力";
    }

    private void LoadToForm(Customer c)
    {
        _editingId      = c.CustomerId;
        EditCode        = c.CustomerCode;
        EditName        = c.CustomerName;
        EditClosingDay  = c.ClosingDay.ToString();
        EditTaxFraction = TaxFractions.FirstOrDefault(f => f.TaxFractionId == c.TaxFractionId);
        EditTaxCalcUnit = TaxCalcUnits.FirstOrDefault(u => u.TaxCalcUnitId  == c.TaxCalcUnitId);
        EditEmployee    = Employees.FirstOrDefault(e => e?.EmployeeId == c.EmployeeId);
        EditPostalCode  = c.PostalCode ?? "";
        EditAddress1    = c.Address1   ?? "";
        EditAddress2    = c.Address2   ?? "";
        StatusMessage   = $"編集中: {c.CustomerName}";
    }

    private async Task OnSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditCode) || string.IsNullOrWhiteSpace(EditName))
        {
            StatusMessage = "コードと名称は必須です";
            return;
        }

        if (!byte.TryParse(EditClosingDay.Trim(), out var cd) ||
            (cd < 1 || cd > 27) && cd != 99)
        {
            StatusMessage = "締日は 1〜27 または 99（月末）を入力してください";
            return;
        }

        if (EditTaxFraction is null)
        {
            StatusMessage = "税端数区分を選択してください";
            return;
        }

        if (EditTaxCalcUnit is null)
        {
            StatusMessage = "税計算単位区分を選択してください";
            return;
        }

        try
        {
            await _repo.UpsertAsync(
                _editingId,
                EditCode.Trim(),
                EditName.Trim(),
                cd,
                EditTaxFraction.TaxFractionId,
                EditTaxCalcUnit.TaxCalcUnitId,
                EditEmployee?.EmployeeId,
                string.IsNullOrWhiteSpace(EditPostalCode) ? null : EditPostalCode.Trim(),
                string.IsNullOrWhiteSpace(EditAddress1)   ? null : EditAddress1.Trim(),
                string.IsNullOrWhiteSpace(EditAddress2)   ? null : EditAddress2.Trim());

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

    private bool CanDelete() => SelectedCustomer is not null;
}
