using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using bmcs_app.Core.Services;
using bmcs_app.PurchaseOrder.Services;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.PurchaseOrder.ViewModels;

public class PurchaseOrderMainViewModel : BindableBase
{
    private readonly LookupService              _lookup;
    private readonly IPurchaseOrderRepository   _purchaseOrderRepo;
    private List<TaxRatePeriod> _taxRatePeriods = new();

    // ── 検索・ナビゲーション ─────────────────────────────────
    private List<string>      _slipNos          = new();
    private int               _currentSlipIndex = -1;
    private List<SlipSummary> _slipSummaries    = new();

    private bool _isLocked;
    public bool IsLocked
    {
        get => _isLocked;
        private set
        {
            if (SetProperty(ref _isLocked, value))
                RaisePropertyChanged(nameof(HasPurchasesText));
        }
    }

    public string HasPurchasesText => IsLocked ? "仕入登録済み" : "未登録";

    private int _totalSlipCount;
    public int TotalSlipCount
    {
        get => _totalSlipCount;
        set => SetProperty(ref _totalSlipCount, value);
    }

    // ── ヘッダー: 日付・伝票No. ──────────────────────────────
    private DateTime? _editPurchaseOrderDate = DateTime.Today;
    public DateTime? EditPurchaseOrderDate
    {
        get => _editPurchaseOrderDate;
        set => SetProperty(ref _editPurchaseOrderDate, value);
    }

    private string _editPurchaseOrderNo = "";
    public string EditPurchaseOrderNo
    {
        get => _editPurchaseOrderNo;
        set => SetProperty(ref _editPurchaseOrderNo, value);
    }

    // ── ヘッダー: 仕入先（コード + 名称） ─────────────────────
    private string _editSupplierCode = "";
    public string EditSupplierCode
    {
        get => _editSupplierCode;
        set => SetProperty(ref _editSupplierCode, value);
    }

    private string _editSupplierName = "";
    public string EditSupplierName
    {
        get => _editSupplierName;
        set => SetProperty(ref _editSupplierName, value);
    }

    private int? _editSupplierId;
    private int  _taxFractionId = 1;

    private bool _isSupplierNameReadOnly = true;
    public bool IsSupplierNameReadOnly
    {
        get => _isSupplierNameReadOnly;
        private set => SetProperty(ref _isSupplierNameReadOnly, value);
    }

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

    // ── 明細 ─────────────────────────────────────────────────
    public ObservableCollection<PurchaseOrderLineViewModel> Lines { get; } = new();

    // ── 集計 ─────────────────────────────────────────────────
    public decimal TaxExcludedTotal => Lines.Sum(l => l.LineAmount);

    public decimal ExternalTaxTotal
        => TaxCalculator.CalcExternalTaxTotal(Lines.Select(ToTaxLine), _taxFractionId);

    private static TaxLineInput ToTaxLine(PurchaseOrderLineViewModel l)
        => new(l.AppliedTaxRate, l.LineAmount);

    public decimal TaxTotal    => ExternalTaxTotal;
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

    public DelegateCommand OpenSupplierLookupCommand   { get; }
    public DelegateCommand OpenEmployeeLookupCommand   { get; }
    public DelegateCommand OpenSlipLookupCommand       { get; }
    public DelegateCommand LookupSupplierByCodeCommand { get; }
    public DelegateCommand LookupEmployeeByCodeCommand { get; }

    // ── コンストラクタ ─────────────────────────────────────
    public PurchaseOrderMainViewModel(LookupService lookup, IPurchaseOrderRepository purchaseOrderRepo)
    {
        _lookup            = lookup;
        _purchaseOrderRepo = purchaseOrderRepo;

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

        OpenSupplierLookupCommand   = new DelegateCommand(OnOpenSupplierLookup);
        OpenEmployeeLookupCommand   = new DelegateCommand(OnOpenEmployeeLookup);
        OpenSlipLookupCommand       = new DelegateCommand(OnOpenSlipLookup);
        LookupSupplierByCodeCommand = new DelegateCommand(OnLookupSupplierByCode);
        LookupEmployeeByCodeCommand = new DelegateCommand(OnLookupEmployeeByCode);

        Lines.CollectionChanged += OnLinesCollectionChanged;

        _ = LoadSlipListAsync();
    }

    // ── 行VM ファクトリ ──────────────────────────────────────
    private PurchaseOrderLineViewModel CreateLineVm(int lineNo) => new PurchaseOrderLineViewModel(
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
    { LineNo = lineNo };

    // ── 行VM のプロパティ変更を購読して集計を再通知 ────────────
    private void OnLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (PurchaseOrderLineViewModel line in e.NewItems)
                line.PropertyChanged += OnLinePropertyChanged;
        if (e.OldItems is not null)
            foreach (PurchaseOrderLineViewModel line in e.OldItems)
                line.PropertyChanged -= OnLinePropertyChanged;
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PurchaseOrderLineViewModel.LineAmount)
                           or nameof(PurchaseOrderLineViewModel.LineCostTotal))
            RaiseTotalsChanged();
    }

    // ── 新規 ─────────────────────────────────────────────────
    private void OnNew()
    {
        IsLocked               = false;
        _currentSlipIndex      = -1;
        EditPurchaseOrderNo    = "";
        EditPurchaseOrderDate  = DateTime.Today;
        EditSupplierCode       = "";
        EditSupplierName       = "";
        _editSupplierId        = null;
        IsSupplierNameReadOnly = true;
        EditEmployeeCode        = "";
        EditEmployeeName        = "";
        _editEmployeeId         = null;
        EditSlipRemarks         = "";
        Lines.Clear();
        RaiseTotalsChanged();
        StatusMessage = "新規伝票";
    }

    // ── 伝票リスト読み込み ───────────────────────────────────
    private async Task LoadSlipListAsync()
    {
        try
        {
            _slipSummaries    = (await _purchaseOrderRepo.GetSummariesAsync()).ToList();
            _slipNos          = _slipSummaries.Select(s => s.SlipNo).ToList();
            TotalSlipCount    = _slipNos.Count;
            if (!string.IsNullOrWhiteSpace(EditPurchaseOrderNo))
                _currentSlipIndex = _slipNos.IndexOf(EditPurchaseOrderNo);
        }
        catch { /* ナビ情報取得失敗は無視 */ }
    }

    // ── 外部からの伝票呼び出し ───────────────────────────────
    public async Task LoadInitialSlipAsync(string purchaseOrderNo)
    {
        EditPurchaseOrderNo = purchaseOrderNo;
        await OnSearchAsync();
    }

    // ── 伝票検索 ─────────────────────────────────────────────
    private async Task OnSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(EditPurchaseOrderNo))
        {
            StatusMessage = "発注No.を入力してください";
            return;
        }
        try
        {
            var slip = await _purchaseOrderRepo.GetByPurchaseOrderNoAsync(EditPurchaseOrderNo.Trim());
            if (slip is null)
            {
                StatusMessage = $"発注No. '{EditPurchaseOrderNo}' が見つかりません";
                return;
            }
            LoadSlip(slip);
        }
        catch (Exception ex)
        {
            StatusMessage = $"発注取得エラー: {ex.Message}";
        }
    }

    private void LoadSlip(PurchaseOrderSlip slip)
    {
        IsLocked              = slip.HasPurchases;
        EditPurchaseOrderNo   = slip.PurchaseOrderNo;
        EditPurchaseOrderDate = slip.PurchaseOrderDate.ToDateTime(TimeOnly.MinValue);
        EditSupplierCode      = slip.SupplierCode;
        EditSupplierName      = slip.SupplierName;
        _editSupplierId       = slip.SupplierId;
        var supplier = _lookup.FindSupplierByCode(slip.SupplierCode);
        _taxFractionId         = supplier?.TaxFractionId ?? 1;
        IsSupplierNameReadOnly = !(supplier?.IsMiscellaneous ?? false);
        EditEmployeeCode      = slip.EmployeeCode;
        EditEmployeeName      = slip.EmployeeName;
        _editEmployeeId       = slip.EmployeeId == 0 ? null : slip.EmployeeId;
        EditSlipRemarks       = slip.SlipRemarks ?? "";

        Lines.Clear();
        foreach (var l in slip.Lines)
        {
            var vm = CreateLineVm(l.LineNo);
            vm.ProductId             = l.ProductId;
            vm.ProductCode           = l.ProductCode;
            vm.ProductName           = l.ProductName;
            vm.IsProductNameReadOnly = !(_lookup.FindProductByCode(l.ProductCode)?.IsMiscellaneous ?? false);
            vm.Quantity              = l.Quantity;
            vm.UnitPrice             = l.UnitPrice;
            vm.CostPrice             = l.CostPrice;
            vm.TaxRateType           = l.TaxRateType;
            vm.AppliedTaxRate        = l.AppliedTaxRate;
            vm.LineRemarks           = l.LineRemarks ?? "";
            Lines.Add(vm);
        }

        RaiseTotalsChanged();
        _currentSlipIndex = _slipNos.IndexOf(slip.PurchaseOrderNo);

        StatusMessage = IsLocked
            ? $"発注No. {slip.PurchaseOrderNo}（仕入登録済み・編集不可）"
            : $"発注No. {slip.PurchaseOrderNo}";
    }

    // ── ナビゲーション ───────────────────────────────────────
    private async Task OnPrevSlipAsync()
    {
        if (_currentSlipIndex <= 0) return;
        _currentSlipIndex--;
        EditPurchaseOrderNo = _slipNos[_currentSlipIndex];
        await OnSearchAsync();
    }

    private async Task OnNextSlipAsync()
    {
        if (_currentSlipIndex >= _slipNos.Count - 1) return;
        _currentSlipIndex++;
        EditPurchaseOrderNo = _slipNos[_currentSlipIndex];
        await OnSearchAsync();
    }

    // ── ルックアップ: 仕入先 ─────────────────────────────────
    private void OnOpenSupplierLookup()
    {
        var result = _lookup.OpenSupplierSearch(EditSupplierCode);
        if (result is not null) ApplySupplier(result);
    }

    private void OnLookupSupplierByCode()
    {
        if (string.IsNullOrWhiteSpace(EditSupplierCode)) return;
        var result = _lookup.FindSupplierByCode(EditSupplierCode);
        if (result is not null)
            ApplySupplier(result);
        else
            StatusMessage = $"仕入先コード '{EditSupplierCode}' が見つかりません";
    }

    private void ApplySupplier(Supplier s)
    {
        EditSupplierCode       = s.SupplierCode;
        EditSupplierName       = s.SupplierName;
        _editSupplierId        = s.SupplierId;
        _taxFractionId         = s.TaxFractionId;
        IsSupplierNameReadOnly = !s.IsMiscellaneous;

        if (s.EmployeeId.HasValue)
        {
            var emp = _lookup.FindEmployeeById(s.EmployeeId.Value);
            if (emp is not null) ApplyEmployee(emp);
        }

        StatusMessage = $"仕入先: {s.SupplierName}";
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
    private void ApplyProductToLine(PurchaseOrderLineViewModel line, Product p)
    {
        line.ProductId              = p.ProductId;
        line.ProductCode            = p.ProductCode;
        line.ProductName            = p.ProductName;
        line.IsProductNameReadOnly  = !p.IsMiscellaneous;
        line.CostPrice              = p.CostPrice;
        line.TaxRateType            = p.TaxRateType;

        var orderDate = EditPurchaseOrderDate.HasValue
            ? DateOnly.FromDateTime(EditPurchaseOrderDate.Value)
            : DateOnly.FromDateTime(DateTime.Today);
        line.AppliedTaxRate = TaxCalculator.GetAppliedTaxRate(_taxRatePeriods, p.TaxRateType, orderDate);

        RaiseTotalsChanged();
        line.RequestMoveToQuantity();
    }

    // ── ルックアップ: 発注No. ────────────────────────────────
    private void OnOpenSlipLookup()
    {
        var selected = _lookup.OpenSlipSearch(EditPurchaseOrderNo);
        if (selected is not null)
        {
            EditPurchaseOrderNo = selected;
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

    private void OnDeleteLine(PurchaseOrderLineViewModel line)
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
            StatusMessage = "仕入登録済みの発注は編集できません";
            return;
        }
        if (!EditPurchaseOrderDate.HasValue)
        {
            StatusMessage = "発注日付を入力してください";
            return;
        }
        if (_editSupplierId is null)
        {
            StatusMessage = "仕入先を指定してください";
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

        var orderDate = DateOnly.FromDateTime(EditPurchaseOrderDate.Value);
        var orderNo   = string.IsNullOrWhiteSpace(EditPurchaseOrderNo)
                            ? GenerateSlipNo(orderDate)
                            : EditPurchaseOrderNo.Trim();

        var slipTaxTotal = ExternalTaxTotal;
        var lineInputs = Lines.Select(l => new PurchaseOrderLineInput(
            l.LineNo, l.ProductId, l.ProductCode, l.ProductName,
            l.Quantity, l.UnitPrice, l.CostPrice,
            1, l.TaxRateType, l.AppliedTaxRate,
            0, slipTaxTotal,
            string.IsNullOrWhiteSpace(l.LineRemarks) ? null : l.LineRemarks));

        try
        {
            await _purchaseOrderRepo.UpsertAsync(
                orderNo,
                orderDate,
                _editSupplierId.Value,
                _editEmployeeId.Value,
                string.IsNullOrWhiteSpace(EditSlipRemarks) ? null : EditSlipRemarks,
                lineInputs);

            EditPurchaseOrderNo = orderNo;
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
        if (string.IsNullOrWhiteSpace(EditPurchaseOrderNo)) return;
        if (_isLocked)
        {
            StatusMessage = "仕入登録済みの発注は削除できません";
            return;
        }

        var result = MessageBox.Show(
            $"発注No. {EditPurchaseOrderNo} を削除しますか？",
            "削除確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _purchaseOrderRepo.DeleteAsync(EditPurchaseOrderNo.Trim());
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
        RaisePropertyChanged(nameof(TaxTotal));
        RaisePropertyChanged(nameof(GrandTotal));
        RaisePropertyChanged(nameof(GrossProfit));
    }
}
