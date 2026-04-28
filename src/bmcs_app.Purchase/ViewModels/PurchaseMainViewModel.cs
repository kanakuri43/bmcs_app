using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using bmcs_app.Core.Services;
using bmcs_app.Purchase.Services;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Purchase.ViewModels;

public class PurchaseMainViewModel : BindableBase
{
    private readonly LookupService           _lookup;
    private readonly IPurchaseRepository     _purchaseRepo;
    private readonly IPurchaseOrderRepository _purchaseOrderRepo;
    private List<TaxRatePeriod> _taxRatePeriods = new();

    // ── 検索・ナビゲーション ─────────────────────────────────
    private List<string>      _slipNos          = new();
    private int               _currentSlipIndex = -1;
    private List<SlipSummary> _slipSummaries    = new();

    private bool _isLocked = false;
    public bool IsLocked
    {
        get => _isLocked;
        private set => SetProperty(ref _isLocked, value);
    }

    private bool _hasSlip = false;
    public bool HasSlip
    {
        get => _hasSlip;
        private set => SetProperty(ref _hasSlip, value);
    }

    private DateOnly? _apClosingAt;
    public string ApClosingAtText => _apClosingAt.HasValue
        ? _apClosingAt.Value.ToString("yyyy/MM/dd")
        : "未集計";

    private int _totalSlipCount;
    public int TotalSlipCount
    {
        get => _totalSlipCount;
        set => SetProperty(ref _totalSlipCount, value);
    }

    // ── ヘッダー: 日付・伝票No・発注No ───────────────────────
    private DateTime? _editPurchaseDate = DateTime.Today;
    public DateTime? EditPurchaseDate
    {
        get => _editPurchaseDate;
        set => SetProperty(ref _editPurchaseDate, value);
    }

    private string _editPurchaseNo = "";
    public string EditPurchaseNo
    {
        get => _editPurchaseNo;
        set => SetProperty(ref _editPurchaseNo, value);
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

    private int?   _editSupplierId;
    private int    _taxFractionId          = 1;
    private string _editSupplierPostalCode = "";

    private bool _isSupplierNameReadOnly = true;
    public bool IsSupplierNameReadOnly
    {
        get => _isSupplierNameReadOnly;
        private set => SetProperty(ref _isSupplierNameReadOnly, value);
    }
    private string _editSupplierAddress1   = "";
    private string _editSupplierAddress2   = "";

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

    // ── ヘッダー: 発注参照 ──────────────────────────────────
    private int? _editPurchaseOrderId;

    // ── ヘッダー: 摘要 ──────────────────────────────────────
    private string _editSlipRemarks = "";
    public string EditSlipRemarks
    {
        get => _editSlipRemarks;
        set => SetProperty(ref _editSlipRemarks, value);
    }

    // ── 明細 ─────────────────────────────────────────────────
    public ObservableCollection<PurchaseLineViewModel> Lines { get; } = new();

    // ── 集計 ─────────────────────────────────────────────────
    public decimal TaxExcludedTotal => Lines.Sum(l => l.LineAmount);

    public decimal ExternalTaxTotal
        => TaxCalculator.CalcExternalTaxTotal(Lines.Select(ToTaxLine), _taxFractionId);

    private static TaxLineInput ToTaxLine(PurchaseLineViewModel l)
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
    public DelegateCommand NewCommand                      { get; }
    public DelegateCommand SearchCommand                   { get; }
    public DelegateCommand PrevSlipCommand                 { get; }
    public DelegateCommand NextSlipCommand                 { get; }
    public DelegateCommand SaveCommand                     { get; }
    public DelegateCommand DeleteSlipCommand               { get; }
    public DelegateCommand AddLineCommand                  { get; }
    public DelegateCommand RemarksEnterCommand             { get; }

    public DelegateCommand OpenSupplierLookupCommand           { get; }
    public DelegateCommand OpenEmployeeLookupCommand           { get; }
    public DelegateCommand OpenPurchaseOrderLookupCommand      { get; }
    public DelegateCommand OpenSlipLookupCommand               { get; }
    public DelegateCommand LookupSupplierByCodeCommand         { get; }
    public DelegateCommand LookupEmployeeByCodeCommand         { get; }
    public DelegateCommand LookupPurchaseOrderByNoCommand      { get; }

    // ── コンストラクタ ─────────────────────────────────────
    public PurchaseMainViewModel(LookupService lookup, IPurchaseRepository purchaseRepo, IPurchaseOrderRepository purchaseOrderRepo)
    {
        _lookup            = lookup;
        _purchaseRepo      = purchaseRepo;
        _purchaseOrderRepo = purchaseOrderRepo;

        NewCommand        = new DelegateCommand(OnNew);
        SearchCommand     = new DelegateCommand(async () => await OnSearchAsync());
        PrevSlipCommand   = new DelegateCommand(async () => await OnPrevSlipAsync());
        NextSlipCommand   = new DelegateCommand(async () => await OnNextSlipAsync());
        SaveCommand       = new DelegateCommand(async () => await OnSaveAsync(), () => !IsLocked)
                              .ObservesProperty(() => IsLocked);
        DeleteSlipCommand = new DelegateCommand(async () => await OnDeleteSlipAsync(), () => !IsLocked)
                              .ObservesProperty(() => IsLocked);
        AddLineCommand      = new DelegateCommand(OnAddLine);
        RemarksEnterCommand = new DelegateCommand(OnRemarksEnter);

        OpenSupplierLookupCommand           = new DelegateCommand(OnOpenSupplierLookup);
        OpenEmployeeLookupCommand           = new DelegateCommand(OnOpenEmployeeLookup);
        OpenPurchaseOrderLookupCommand      = new DelegateCommand(OnOpenPurchaseOrderLookup);
        OpenSlipLookupCommand               = new DelegateCommand(OnOpenSlipLookup);
        LookupSupplierByCodeCommand         = new DelegateCommand(OnLookupSupplierByCode);
        LookupEmployeeByCodeCommand         = new DelegateCommand(OnLookupEmployeeByCode);
        LookupPurchaseOrderByNoCommand      = new DelegateCommand(async () => await OnLookupPurchaseOrderByNoAsync());

        Lines.CollectionChanged += OnLinesCollectionChanged;

        _ = LoadSlipListAsync();
    }

    // ── 行VM ファクトリ ──────────────────────────────────────
    private PurchaseLineViewModel CreateLineVm(int lineNo) => new PurchaseLineViewModel(
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
            foreach (PurchaseLineViewModel line in e.NewItems)
                line.PropertyChanged += OnLinePropertyChanged;
        if (e.OldItems is not null)
            foreach (PurchaseLineViewModel line in e.OldItems)
                line.PropertyChanged -= OnLinePropertyChanged;
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PurchaseLineViewModel.LineAmount)
                           or nameof(PurchaseLineViewModel.LineCostTotal))
            RaiseTotalsChanged();
    }

    // ── 新規 ─────────────────────────────────────────────────
    private void OnNew()
    {
        IsLocked              = false;
        HasSlip               = false;
        _apClosingAt          = null;
        RaisePropertyChanged(nameof(ApClosingAtText));
        _currentSlipIndex     = -1;
        EditPurchaseNo         = "";
        EditPurchaseOrderNo    = "";
        EditPurchaseDate       = DateTime.Today;
        EditSupplierCode       = "";
        EditSupplierName       = "";
        _editSupplierId        = null;
        IsSupplierNameReadOnly = true;
        EditEmployeeCode      = "";
        EditEmployeeName      = "";
        _editEmployeeId       = null;
        _editPurchaseOrderId  = null;
        EditSlipRemarks       = "";
        Lines.Clear();
        RaiseTotalsChanged();
        StatusMessage = "新規伝票";
    }

    // ── 伝票リスト読み込み ───────────────────────────────────
    private async Task LoadSlipListAsync()
    {
        try
        {
            _slipSummaries    = (await _purchaseRepo.GetSummariesAsync()).ToList();
            _slipNos          = _slipSummaries.Select(s => s.SlipNo).ToList();
            TotalSlipCount    = _slipNos.Count;
            if (!string.IsNullOrWhiteSpace(EditPurchaseNo))
                _currentSlipIndex = _slipNos.IndexOf(EditPurchaseNo);
        }
        catch { /* ナビ情報取得失敗は無視 */ }
    }

    // ── 外部からの伝票呼び出し ───────────────────────────────
    public async Task LoadInitialSlipAsync(string purchaseNo)
    {
        EditPurchaseNo = purchaseNo;
        await OnSearchAsync();
    }

    // ── 伝票検索 ─────────────────────────────────────────────
    private async Task OnSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(EditPurchaseNo))
        {
            StatusMessage = "伝票No.を入力してください";
            return;
        }
        try
        {
            var slip = await _purchaseRepo.GetByPurchaseNoAsync(EditPurchaseNo.Trim());
            if (slip is null)
            {
                StatusMessage = $"伝票No. '{EditPurchaseNo}' が見つかりません";
                return;
            }
            LoadSlip(slip);
        }
        catch (Exception ex)
        {
            StatusMessage = $"伝票取得エラー: {ex.Message}";
        }
    }

    private void LoadSlip(PurchaseSlip slip)
    {
        IsLocked   = slip.IsLocked;
        HasSlip    = true;
        _apClosingAt = slip.ApClosingAt;
        RaisePropertyChanged(nameof(ApClosingAtText));

        _editSupplierPostalCode = slip.SupplierPostalCode ?? "";
        _editSupplierAddress1   = slip.SupplierAddress1   ?? "";
        _editSupplierAddress2   = slip.SupplierAddress2   ?? "";
        EditPurchaseNo        = slip.PurchaseNo;
        EditPurchaseDate      = slip.PurchaseDate.ToDateTime(TimeOnly.MinValue);
        EditSupplierCode      = slip.SupplierCode;
        EditSupplierName      = slip.SupplierName;
        _editSupplierId       = slip.SupplierId;
        var supplier = _lookup.FindSupplierByCode(slip.SupplierCode);
        _taxFractionId        = supplier?.TaxFractionId ?? 1;
        IsSupplierNameReadOnly = !(supplier?.IsMiscellaneous ?? false);
        EditEmployeeCode      = slip.EmployeeCode;
        EditEmployeeName      = slip.EmployeeName;
        _editEmployeeId       = slip.EmployeeId;
        EditPurchaseOrderNo   = slip.PurchaseOrderNo ?? "";
        _editPurchaseOrderId  = slip.PurchaseOrderId;
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
        _currentSlipIndex = _slipNos.IndexOf(slip.PurchaseNo);

        StatusMessage = _isLocked
            ? $"伝票No. {slip.PurchaseNo}（支払集計済み・編集不可）"
            : $"伝票No. {slip.PurchaseNo}";
    }

    // ── ナビゲーション ───────────────────────────────────────
    private async Task OnPrevSlipAsync()
    {
        if (_currentSlipIndex <= 0) return;
        _currentSlipIndex--;
        EditPurchaseNo = _slipNos[_currentSlipIndex];
        await OnSearchAsync();
    }

    private async Task OnNextSlipAsync()
    {
        if (_currentSlipIndex >= _slipNos.Count - 1) return;
        _currentSlipIndex++;
        EditPurchaseNo = _slipNos[_currentSlipIndex];
        await OnSearchAsync();
    }

    // ── ルックアップ: 仕入先 ─────────────────────────────────
    private void OnOpenSupplierLookup()
    {
        var result = _lookup.OpenSupplierSearch(EditSupplierCode);
        if (result is not null)
            ApplySupplier(result);
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
        EditSupplierCode         = s.SupplierCode;
        EditSupplierName         = s.SupplierName;
        _editSupplierId          = s.SupplierId;
        _taxFractionId           = s.TaxFractionId;
        _editSupplierPostalCode  = s.PostalCode ?? "";
        _editSupplierAddress1    = s.Address1   ?? "";
        _editSupplierAddress2    = s.Address2   ?? "";
        IsSupplierNameReadOnly   = !s.IsMiscellaneous;

        if (s.EmployeeId.HasValue)
        {
            var emp = _lookup.FindEmployeeById(s.EmployeeId.Value);
            if (emp is not null) ApplyEmployee(emp);
        }

        StatusMessage = s.IsMiscellaneous
            ? "諸口仕入先：適格請求書なし。仕入税額控除の対象外です。"
            : $"仕入先: {s.SupplierName}";
    }

    // ── ルックアップ: 担当者 ─────────────────────────────────
    private void OnOpenEmployeeLookup()
    {
        var result = _lookup.OpenEmployeeSearch(EditEmployeeCode);
        if (result is not null)
            ApplyEmployee(result);
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
    private void ApplyProductToLine(PurchaseLineViewModel line, Product p)
    {
        line.ProductId              = p.ProductId;
        line.ProductCode            = p.ProductCode;
        line.ProductName            = p.ProductName;
        line.IsProductNameReadOnly  = !p.IsMiscellaneous;
        line.CostPrice              = p.CostPrice;
        line.TaxRateType            = p.TaxRateType;

        var purchaseDate = EditPurchaseDate.HasValue
            ? DateOnly.FromDateTime(EditPurchaseDate.Value)
            : DateOnly.FromDateTime(DateTime.Today);
        line.AppliedTaxRate = TaxCalculator.GetAppliedTaxRate(_taxRatePeriods, p.TaxRateType, purchaseDate);

        RaiseTotalsChanged();
        line.RequestMoveToQuantity();
    }

    // ── ルックアップ: 伝票番号 ───────────────────────────────
    private void OnOpenSlipLookup()
    {
        var selected = _lookup.OpenSlipSearch(EditPurchaseNo);
        if (selected is not null)
        {
            EditPurchaseNo = selected;
            _ = OnSearchAsync();
        }
    }

    // ── ルックアップ: 発注No. ────────────────────────────────
    private async Task OnLookupPurchaseOrderByNoAsync()
    {
        if (string.IsNullOrWhiteSpace(EditPurchaseOrderNo)) return;
        try
        {
            var order = await _purchaseOrderRepo.GetByPurchaseOrderNoAsync(EditPurchaseOrderNo.Trim());
            if (order is null)
            {
                StatusMessage = $"発注No. '{EditPurchaseOrderNo}' が見つかりません";
                return;
            }
            ApplyPurchaseOrder(order);
        }
        catch (Exception ex)
        {
            StatusMessage = $"発注取得エラー: {ex.Message}";
        }
    }

    private void OnOpenPurchaseOrderLookup()
    {
        var selected = _lookup.OpenPurchaseOrderSearch(EditPurchaseOrderNo);
        if (selected is not null)
        {
            EditPurchaseOrderNo = selected;
            _ = LoadPurchaseOrderByNoAsync(selected);
        }
    }

    private async Task LoadPurchaseOrderByNoAsync(string purchaseOrderNo)
    {
        try
        {
            var order = await _purchaseOrderRepo.GetByPurchaseOrderNoAsync(purchaseOrderNo);
            if (order is not null) ApplyPurchaseOrder(order);
        }
        catch (Exception ex)
        {
            StatusMessage = $"発注取得エラー: {ex.Message}";
        }
    }

    private void ApplyPurchaseOrder(PurchaseOrderSlip order)
    {
        _editPurchaseOrderId = order.PurchaseOrderId;
        EditPurchaseOrderNo  = order.PurchaseOrderNo;

        var supplier = _lookup.FindSupplierByCode(order.SupplierCode);
        if (supplier is not null)
        {
            ApplySupplier(supplier);
        }
        else
        {
            EditSupplierCode = order.SupplierCode;
            EditSupplierName = order.SupplierName;
            _editSupplierId  = order.SupplierId;
        }

        var employee = order.EmployeeId != 0 ? _lookup.FindEmployeeById(order.EmployeeId) : null;
        if (employee is not null)
            ApplyEmployee(employee);

        EditSlipRemarks = order.SlipRemarks ?? "";

        Lines.Clear();
        var purchaseDate = EditPurchaseDate.HasValue
            ? DateOnly.FromDateTime(EditPurchaseDate.Value)
            : DateOnly.FromDateTime(DateTime.Today);

        foreach (var l in order.Lines)
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
            vm.AppliedTaxRate        = TaxCalculator.GetAppliedTaxRate(_taxRatePeriods, l.TaxRateType, purchaseDate);
            vm.LineRemarks           = l.LineRemarks ?? "";
            Lines.Add(vm);
        }

        RaiseTotalsChanged();
        StatusMessage = $"発注No. {order.PurchaseOrderNo} を読み込みました";
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

    private void OnDeleteLine(PurchaseLineViewModel line)
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
            StatusMessage = "支払集計済み伝票は編集できません";
            return;
        }
        if (!EditPurchaseDate.HasValue)
        {
            StatusMessage = "仕入日付を入力してください";
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

        var purchaseDate = DateOnly.FromDateTime(EditPurchaseDate.Value);
        var purchaseNo   = string.IsNullOrWhiteSpace(EditPurchaseNo)
                               ? GenerateSlipNo(purchaseDate)
                               : EditPurchaseNo.Trim();

        var slipTaxTotal = ExternalTaxTotal;
        var lineInputs = Lines.Select(l => new PurchaseLineInput(
            l.LineNo, l.ProductId, l.ProductCode, l.ProductName,
            l.Quantity, l.UnitPrice, l.CostPrice,
            1, l.TaxRateType, l.AppliedTaxRate,
            0, slipTaxTotal,
            string.IsNullOrWhiteSpace(l.LineRemarks) ? null : l.LineRemarks));

        try
        {
            await _purchaseRepo.UpsertAsync(
                purchaseNo,
                DateOnly.FromDateTime(EditPurchaseDate.Value),
                _editSupplierId.Value,
                _editPurchaseOrderId,
                string.IsNullOrWhiteSpace(EditPurchaseOrderNo) ? null : EditPurchaseOrderNo,
                _editEmployeeId.Value,
                string.IsNullOrWhiteSpace(EditSlipRemarks) ? null : EditSlipRemarks,
                lineInputs);

            EditPurchaseNo = purchaseNo;
            StatusMessage  = "登録しました";
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
        if (string.IsNullOrWhiteSpace(EditPurchaseNo)) return;
        if (_isLocked)
        {
            StatusMessage = "支払集計済み伝票は削除できません";
            return;
        }

        var result = MessageBox.Show(
            $"伝票No. {EditPurchaseNo} を削除しますか？",
            "削除確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _purchaseRepo.DeleteAsync(EditPurchaseNo.Trim());
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
