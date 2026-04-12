using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using bmcs_app.Core.Services;
using bmcs_app.Order.Services;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Order.ViewModels;

public class OrderMainViewModel : BindableBase
{
    private readonly LookupService   _lookup;
    private readonly IOrderRepository _orderRepo;
    private List<TaxRatePeriod> _taxRatePeriods = new();
    private bool _isLineTaxCalc = true;

    // ── 検索・ナビゲーション ─────────────────────────────────
    private List<string> _slipNos          = new();
    private int          _currentSlipIndex = -1;
    private List<SlipSummary> _slipSummaries = new();

    private bool _isLocked;
    public bool IsLocked
    {
        get => _isLocked;
        private set
        {
            if (SetProperty(ref _isLocked, value))
                RaisePropertyChanged(nameof(HasSalesText));
        }
    }

    public string HasSalesText => IsLocked ? "売上登録済み" : "未登録";

    private int _totalSlipCount;
    public int TotalSlipCount
    {
        get => _totalSlipCount;
        set => SetProperty(ref _totalSlipCount, value);
    }

    // ── ヘッダー: 日付・伝票No. ──────────────────────────────
    private DateTime? _editOrderDate = DateTime.Today;
    public DateTime? EditOrderDate
    {
        get => _editOrderDate;
        set => SetProperty(ref _editOrderDate, value);
    }

    private string _editOrderNo = "";
    public string EditOrderNo
    {
        get => _editOrderNo;
        set => SetProperty(ref _editOrderNo, value);
    }

    // ── ヘッダー: 得意先（コード + 名称） ─────────────────────
    private string _editCustomerCode = "";
    public string EditCustomerCode
    {
        get => _editCustomerCode;
        set => SetProperty(ref _editCustomerCode, value);
    }

    private string _editCustomerName = "";
    public string EditCustomerName
    {
        get => _editCustomerName;
        set => SetProperty(ref _editCustomerName, value);
    }

    private int? _editCustomerId;

    // ── ヘッダー: 担当者（コード + 名称） ─────────────────────
    private string _editEmployeeCode = "";
    public string EditEmployeeCode
    {
        get => _editEmployeeCode;
        set => SetProperty(ref _editEmployeeCode, value);
    }

    private string _editEmployeeName = "";
    public string EditEmployeeName
    {
        get => _editEmployeeName;
        set => SetProperty(ref _editEmployeeName, value);
    }

    private int? _editEmployeeId;

    // ── ヘッダー: 摘要 ──────────────────────────────────────
    private string _editSlipRemarks = "";
    public string EditSlipRemarks
    {
        get => _editSlipRemarks;
        set => SetProperty(ref _editSlipRemarks, value);
    }

    // ── 税種別（明細 ComboBox 用） ──────────────────────────
    public ObservableCollection<TaxTypeClassification> TaxTypes { get; } = new();

    // ── 明細 ─────────────────────────────────────────────────
    public ObservableCollection<OrderLineViewModel> Lines { get; } = new();

    // ── 集計 ─────────────────────────────────────────────────
    public decimal TaxExcludedTotal => Lines.Sum(l => l.LineAmount);

    public decimal ExternalTaxTotal
        => TaxCalculator.CalcExternalTaxTotal(Lines.Select(ToTaxLine), _isLineTaxCalc);

    public decimal InternalTaxTotal
        => TaxCalculator.CalcInternalTaxTotal(Lines.Select(ToTaxLine), _isLineTaxCalc);

    private static TaxLineInput ToTaxLine(OrderLineViewModel l)
        => new(l.TaxType?.TaxTypeId ?? 0, l.AppliedTaxRate, l.LineAmount, l.LineTaxAmount);

    public decimal TaxTotal    => ExternalTaxTotal + InternalTaxTotal;
    public decimal GrandTotal  => TaxExcludedTotal + ExternalTaxTotal;
    public decimal GrossProfit => TaxExcludedTotal - Lines.Sum(l => l.LineCostTotal);

    // ── ステータス ───────────────────────────────────────────
    private string _statusMessage = "準備完了";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // ── フォーカス移動イベント ───────────────────────────────
    public event Action<string>? FocusField;

    public static class FocusTargets
    {
        public const string LineProductCode     = "LineProductCode";
        public const string LineProductCodeLast = "LineProductCodeLast";
    }

    // ── コマンド ─────────────────────────────────────────────
    public DelegateCommand NewCommand             { get; }
    public DelegateCommand SearchCommand          { get; }
    public DelegateCommand PrevSlipCommand        { get; }
    public DelegateCommand NextSlipCommand        { get; }
    public DelegateCommand SaveCommand            { get; }
    public DelegateCommand DeleteSlipCommand      { get; }
    public DelegateCommand AddLineCommand         { get; }
    public DelegateCommand RemarksEnterCommand    { get; }

    public DelegateCommand OpenCustomerLookupCommand   { get; }
    public DelegateCommand OpenEmployeeLookupCommand   { get; }
    public DelegateCommand OpenSlipLookupCommand       { get; }
    public DelegateCommand LookupCustomerByCodeCommand { get; }
    public DelegateCommand LookupEmployeeByCodeCommand { get; }

    // ── コンストラクタ ─────────────────────────────────────
    public OrderMainViewModel(LookupService lookup, IOrderRepository orderRepo)
    {
        _lookup    = lookup;
        _orderRepo = orderRepo;

        NewCommand          = new DelegateCommand(OnNew);
        SearchCommand       = new DelegateCommand(async () => await OnSearchAsync());
        PrevSlipCommand     = new DelegateCommand(async () => await OnPrevSlipAsync());
        NextSlipCommand     = new DelegateCommand(async () => await OnNextSlipAsync());
        SaveCommand         = new DelegateCommand(async () => await OnSaveAsync(), () => !IsLocked)
                                .ObservesProperty(() => IsLocked);
        DeleteSlipCommand   = new DelegateCommand(async () => await OnDeleteSlipAsync(), () => !IsLocked)
                                .ObservesProperty(() => IsLocked);
        AddLineCommand      = new DelegateCommand(OnAddLine);
        RemarksEnterCommand = new DelegateCommand(OnRemarksEnter);

        OpenCustomerLookupCommand   = new DelegateCommand(OnOpenCustomerLookup);
        OpenEmployeeLookupCommand   = new DelegateCommand(OnOpenEmployeeLookup);
        OpenSlipLookupCommand       = new DelegateCommand(OnOpenSlipLookup);
        LookupCustomerByCodeCommand = new DelegateCommand(OnLookupCustomerByCode);
        LookupEmployeeByCodeCommand = new DelegateCommand(OnLookupEmployeeByCode);

        Lines.CollectionChanged += OnLinesCollectionChanged;

        _ = LoadSlipListAsync();
    }

    // ── 行VM ファクトリ ──────────────────────────────────────
    private OrderLineViewModel CreateLineVm(int lineNo) => new OrderLineViewModel(
        onOpenProductLookup: line =>
        {
            var result = _lookup.OpenProductSearch(line.ProductCode);
            if (result is not null) ApplyProductToLine(line, result);
        },
        onLookupProductByCode: line =>
        {
            var result = _lookup.FindProductByCode(line.ProductCode);
            if (result is not null)
                ApplyProductToLine(line, result);
            else if (!string.IsNullOrWhiteSpace(line.ProductCode))
                StatusMessage = $"商品コード '{line.ProductCode}' が見つかりません";
        },
        onDelete: OnDeleteLine,
        onLineRemarksEnter: _ =>
        {
            OnAddLine();
            FocusField?.Invoke(FocusTargets.LineProductCodeLast);
        }
    )
    { LineNo = lineNo, IsLineTaxCalc = _isLineTaxCalc };

    // ── 行VM のプロパティ変更を購読して集計を再通知 ────────────
    private void OnLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (OrderLineViewModel line in e.NewItems)
                line.PropertyChanged += OnLinePropertyChanged;
        if (e.OldItems is not null)
            foreach (OrderLineViewModel line in e.OldItems)
                line.PropertyChanged -= OnLinePropertyChanged;
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OrderLineViewModel.LineAmount)
                           or nameof(OrderLineViewModel.LineTaxAmount)
                           or nameof(OrderLineViewModel.TaxType)
                           or nameof(OrderLineViewModel.LineCostTotal))
            RaiseTotalsChanged();
    }

    // ── 新規 ─────────────────────────────────────────────────
    private void OnNew()
    {
        IsLocked          = false;
        _currentSlipIndex = -1;
        EditOrderNo       = "";
        EditOrderDate     = DateTime.Today;
        EditCustomerCode  = "";
        EditCustomerName  = "";
        _editCustomerId   = null;
        EditEmployeeCode  = "";
        EditEmployeeName  = "";
        _editEmployeeId   = null;
        EditSlipRemarks   = "";
        Lines.Clear();
        RaiseTotalsChanged();
        StatusMessage = "新規伝票";
    }

    // ── 伝票リスト読み込み ───────────────────────────────────
    private async Task LoadSlipListAsync()
    {
        try
        {
            _slipSummaries    = (await _orderRepo.GetSummariesAsync()).ToList();
            _slipNos          = _slipSummaries.Select(s => s.SlipNo).ToList();
            TotalSlipCount    = _slipNos.Count;
            if (!string.IsNullOrWhiteSpace(EditOrderNo))
                _currentSlipIndex = _slipNos.IndexOf(EditOrderNo);
        }
        catch { /* ナビ情報取得失敗は無視 */ }
    }

    // ── 外部からの伝票呼び出し ───────────────────────────────
    public async Task LoadInitialSlipAsync(string orderNo)
    {
        EditOrderNo = orderNo;
        await OnSearchAsync();
    }

    // ── 伝票検索 ─────────────────────────────────────────────
    private async Task OnSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(EditOrderNo))
        {
            StatusMessage = "受注No.を入力してください";
            return;
        }
        try
        {
            var slip = await _orderRepo.GetByOrderNoAsync(EditOrderNo.Trim());
            if (slip is null)
            {
                StatusMessage = $"受注No. '{EditOrderNo}' が見つかりません";
                return;
            }
            LoadSlip(slip);
        }
        catch (Exception ex)
        {
            StatusMessage = $"受注取得エラー: {ex.Message}";
        }
    }

    private void LoadSlip(OrderSlip slip)
    {
        IsLocked         = slip.HasSales;
        _isLineTaxCalc   = TaxCalculator.IsLineTaxCalc(slip.TaxCalcUnitId);
        EditOrderNo      = slip.OrderNo;
        EditOrderDate    = slip.OrderDate.ToDateTime(TimeOnly.MinValue);
        EditCustomerCode = slip.CustomerCode;
        EditCustomerName = slip.CustomerName;
        _editCustomerId  = slip.CustomerId;
        EditEmployeeCode = slip.EmployeeCode;
        EditEmployeeName = slip.EmployeeName;
        _editEmployeeId  = slip.EmployeeId == 0 ? null : slip.EmployeeId;
        EditSlipRemarks  = slip.SlipRemarks ?? "";

        Lines.Clear();
        foreach (var l in slip.Lines)
        {
            var taxType = TaxTypes.FirstOrDefault(t => t.TaxTypeId == l.TaxTypeId);
            var vm = CreateLineVm(l.LineNo);
            vm.ProductId      = l.ProductId;
            vm.ProductCode    = l.ProductCode;
            vm.ProductName    = l.ProductName;
            vm.Quantity       = l.Quantity;
            vm.UnitPrice      = l.UnitPrice;
            vm.CostPrice      = l.CostPrice;
            vm.TaxType        = taxType;
            vm.TaxRateType    = l.TaxRateType;
            vm.AppliedTaxRate = l.AppliedTaxRate;
            vm.LineRemarks    = l.LineRemarks ?? "";
            Lines.Add(vm);
        }

        RaiseTotalsChanged();
        _currentSlipIndex = _slipNos.IndexOf(slip.OrderNo);

        StatusMessage = IsLocked
            ? $"受注No. {slip.OrderNo}（売上登録済み・編集不可）"
            : $"受注No. {slip.OrderNo}";
    }

    // ── ナビゲーション ───────────────────────────────────────
    private async Task OnPrevSlipAsync()
    {
        if (_currentSlipIndex <= 0) return;
        _currentSlipIndex--;
        EditOrderNo = _slipNos[_currentSlipIndex];
        await OnSearchAsync();
    }

    private async Task OnNextSlipAsync()
    {
        if (_currentSlipIndex >= _slipNos.Count - 1) return;
        _currentSlipIndex++;
        EditOrderNo = _slipNos[_currentSlipIndex];
        await OnSearchAsync();
    }

    // ── ルックアップ: 得意先 ─────────────────────────────────
    private void OnOpenCustomerLookup()
    {
        var result = _lookup.OpenCustomerSearch(EditCustomerCode);
        if (result is not null) ApplyCustomer(result);
    }

    private void OnLookupCustomerByCode()
    {
        if (string.IsNullOrWhiteSpace(EditCustomerCode)) return;
        var result = _lookup.FindCustomerByCode(EditCustomerCode);
        if (result is not null)
            ApplyCustomer(result);
        else
            StatusMessage = $"得意先コード '{EditCustomerCode}' が見つかりません";
    }

    private void ApplyCustomer(Customer c)
    {
        EditCustomerCode  = c.CustomerCode;
        EditCustomerName  = c.CustomerName;
        _editCustomerId   = c.CustomerId;
        _isLineTaxCalc    = TaxCalculator.IsLineTaxCalc(c.TaxCalcUnitId);
        PropagateLineTaxCalcToLines();

        if (c.EmployeeId.HasValue)
        {
            var emp = _lookup.FindEmployeeById(c.EmployeeId.Value);
            if (emp is not null) ApplyEmployee(emp);
        }

        StatusMessage = $"得意先: {c.CustomerName}";
    }

    // ── ルックアップ: 担当者 ─────────────────────────────────
    private void OnOpenEmployeeLookup()
    {
        var result = _lookup.OpenEmployeeSearch(EditEmployeeCode);
        if (result is not null) ApplyEmployee(result);
    }

    private void OnLookupEmployeeByCode()
    {
        if (string.IsNullOrWhiteSpace(EditEmployeeCode)) return;
        var result = _lookup.FindEmployeeByCode(EditEmployeeCode);
        if (result is not null)
            ApplyEmployee(result);
        else
            StatusMessage = $"担当者コード '{EditEmployeeCode}' が見つかりません";
    }

    private void ApplyEmployee(Employee e)
    {
        EditEmployeeCode = e.EmployeeCode;
        EditEmployeeName = e.EmployeeName;
        _editEmployeeId  = e.EmployeeId;
        StatusMessage    = $"担当者: {e.EmployeeName}";
    }

    // ── ルックアップ: 商品（明細行コールバック） ─────────────
    private void ApplyProductToLine(OrderLineViewModel line, Product p)
    {
        line.ProductId   = p.ProductId;
        line.ProductCode = p.ProductCode;
        line.ProductName = p.ProductName;
        line.CostPrice   = p.CostPrice;
        line.TaxRateType = p.TaxRateType;
        var taxType = TaxTypes.FirstOrDefault(t => t.TaxTypeId == p.TaxTypeId);
        line.TaxType = taxType;

        var orderDate = EditOrderDate.HasValue
            ? DateOnly.FromDateTime(EditOrderDate.Value)
            : DateOnly.FromDateTime(DateTime.Today);
        line.AppliedTaxRate = TaxCalculator.GetAppliedTaxRate(_taxRatePeriods, p.TaxRateType, orderDate);

        RaiseTotalsChanged();
        line.RequestMoveToQuantity();
    }

    // ── ルックアップ: 受注No. ────────────────────────────────
    private void OnOpenSlipLookup()
    {
        var selected = _lookup.OpenSlipSearch(_slipSummaries, EditOrderNo);
        if (selected is not null)
        {
            EditOrderNo = selected;
            _ = OnSearchAsync();
        }
    }

    // ── 摘要 Enter ──────────────────────────────────────────
    private void OnRemarksEnter()
    {
        if (Lines.Count == 0)
            OnAddLine();
        FocusField?.Invoke(FocusTargets.LineProductCode);
    }

    // ── 明細行操作 ──────────────────────────────────────────
    private void OnAddLine()
    {
        var line = CreateLineVm(Lines.Count + 1);
        Lines.Add(line);
        RaiseTotalsChanged();
    }

    private void OnDeleteLine(OrderLineViewModel line)
    {
        Lines.Remove(line);
        for (int i = 0; i < Lines.Count; i++)
            Lines[i].LineNo = i + 1;
        RaiseTotalsChanged();
        StatusMessage = "行を削除しました";
    }

    // ── 保存 ─────────────────────────────────────────────────
    private async Task OnSaveAsync()
    {
        if (_isLocked)
        {
            StatusMessage = "売上登録済みの受注は編集できません";
            return;
        }
        if (!EditOrderDate.HasValue)
        {
            StatusMessage = "受注日付を入力してください";
            return;
        }
        if (_editCustomerId is null)
        {
            StatusMessage = "得意先を指定してください";
            return;
        }
        if (_editEmployeeId is null)
        {
            StatusMessage = "担当者を指定してください";
            return;
        }
        if (Lines.Count == 0)
        {
            StatusMessage = "明細行を1件以上入力してください";
            return;
        }
        if (Lines.Any(l => l.ProductId == 0))
        {
            StatusMessage = "商品が未設定の行があります";
            return;
        }

        var orderDate = DateOnly.FromDateTime(EditOrderDate.Value);
        var orderNo   = string.IsNullOrWhiteSpace(EditOrderNo)
                            ? GenerateSlipNo(orderDate)
                            : EditOrderNo.Trim();

        var slipTaxTotal = Lines.Sum(l => l.LineTaxAmount);
        var lineInputs = Lines.Select(l => new OrderLineInput(
            l.LineNo, l.ProductId, l.ProductCode, l.ProductName,
            l.Quantity, l.UnitPrice, l.CostPrice,
            l.TaxType?.TaxTypeId ?? 0, l.TaxRateType, l.AppliedTaxRate,
            l.LineTaxAmount, slipTaxTotal,
            string.IsNullOrWhiteSpace(l.LineRemarks) ? null : l.LineRemarks));

        try
        {
            await _orderRepo.UpsertAsync(
                orderNo,
                orderDate,
                _editCustomerId.Value,
                _editEmployeeId.Value,
                string.IsNullOrWhiteSpace(EditSlipRemarks) ? null : EditSlipRemarks,
                lineInputs);

            EditOrderNo   = orderNo;
            StatusMessage = "登録しました";
            await LoadSlipListAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
        }
    }

    // ── 削除 ─────────────────────────────────────────────────
    private async Task OnDeleteSlipAsync()
    {
        if (string.IsNullOrWhiteSpace(EditOrderNo)) return;
        if (_isLocked)
        {
            StatusMessage = "売上登録済みの受注は削除できません";
            return;
        }

        var result = MessageBox.Show(
            $"受注No. {EditOrderNo} を削除しますか？",
            "削除確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _orderRepo.DeleteAsync(EditOrderNo.Trim());
            StatusMessage = "削除しました";
            OnNew();
            await LoadSlipListAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"削除エラー: {ex.Message}";
        }
    }

    // ── 税率期間マスタ ───────────────────────────────────────
    public void SetTaxRatePeriods(IEnumerable<TaxRatePeriod> periods)
        => _taxRatePeriods = periods.ToList();

    // ── 税計算単位 ───────────────────────────────────────────
    private void PropagateLineTaxCalcToLines()
    {
        foreach (var line in Lines)
            line.IsLineTaxCalc = _isLineTaxCalc;
        RaiseTotalsChanged();
    }

    // ── 伝票番号自動生成 ─────────────────────────────────────
    private string GenerateSlipNo(DateOnly date)
    {
        var prefix = date.ToString("yyyyMMdd");
        var count  = _slipNos.Count(n => n.StartsWith(prefix));
        return $"{prefix}{count + 1:000}";
    }

    // ── 集計再通知 ───────────────────────────────────────────
    public void RaiseTotalsChanged()
    {
        RaisePropertyChanged(nameof(TaxExcludedTotal));
        RaisePropertyChanged(nameof(ExternalTaxTotal));
        RaisePropertyChanged(nameof(InternalTaxTotal));
        RaisePropertyChanged(nameof(TaxTotal));
        RaisePropertyChanged(nameof(GrandTotal));
        RaisePropertyChanged(nameof(GrossProfit));
    }
}
