using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using bmcs_app.Infrastructure;
using bmcs_app.Sales.Services;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Sales.ViewModels;

public class SalesMainViewModel : BindableBase
{
    private readonly ILookupService   _lookup;
    private readonly ISaleRepository  _saleRepo;
    private readonly IOrderRepository _orderRepo;
    private List<TaxRatePeriod> _taxRatePeriods = new();
    private CompanyInfo _companyInfo = new();
    private bool _isLineTaxCalc = true;

    // ── 検索・ナビゲーション ─────────────────────────────────
    private List<string> _slipNos         = new();
    private int          _currentSlipIndex = -1;

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

    private DateOnly? _invoicedAt;
    private DateOnly? _arAggregatedAt;

    public string InvoicedAtText      => _invoicedAt.HasValue     ? _invoicedAt.Value.ToString("yyyy/MM/dd")     : "未集計";
    public string ArAggregatedAtText  => _arAggregatedAt.HasValue ? _arAggregatedAt.Value.ToString("yyyy/MM/dd") : "未集計";

    private int _totalSlipCount;
    public int TotalSlipCount
    {
        get => _totalSlipCount;
        set => SetProperty(ref _totalSlipCount, value);
    }

    // ── ヘッダー: 日付・伝票No・受注No ───────────────────────
    private DateTime? _editSaleDate = DateTime.Today;
    public DateTime? EditSaleDate
    {
        get => _editSaleDate;
        set => SetProperty(ref _editSaleDate, value);
    }

    private string _editSaleNo = "";
    public string EditSaleNo
    {
        get => _editSaleNo;
        set => SetProperty(ref _editSaleNo, value);
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

    private int?   _editCustomerId;
    private string _editCustomerPostalCode = "";
    private string _editCustomerAddress1   = "";
    private string _editCustomerAddress2   = "";

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

    // ── ヘッダー: 受注参照 ──────────────────────────────────
    private int? _editOrderId;

    // ── ヘッダー: 摘要 ──────────────────────────────────────
    private string _editSlipRemarks = "";
    public string EditSlipRemarks
    {
        get => _editSlipRemarks;
        set => SetProperty(ref _editSlipRemarks, value);
    }

    // ── マスタリスト（ダイアログ用に保持、表示には使わない） ──
    public ObservableCollection<TaxTypeClassification> TaxTypes { get; } = new();

    // ── 明細 ─────────────────────────────────────────────────
    public ObservableCollection<SaleLineViewModel> Lines { get; } = new();

    // ── 集計（明細変更のたびに再計算） ──────────────────────
    public decimal TaxExcludedTotal => Lines.Sum(l => l.LineAmount);

    public decimal ExternalTaxTotal
    {
        get
        {
            var externalLines = Lines.Where(l => l.TaxType?.TaxTypeId == 1 && l.AppliedTaxRate > 0);
            if (_isLineTaxCalc)
                return externalLines.Sum(l => l.LineTaxAmount);
            // 伝票単位: 税率ごとに合計金額を集計してから floor
            return externalLines
                .GroupBy(l => l.AppliedTaxRate)
                .Sum(g => Math.Floor(g.Sum(l => l.LineAmount) * g.Key));
        }
    }

    public decimal InternalTaxTotal
    {
        get
        {
            var internalLines = Lines.Where(l => l.TaxType?.TaxTypeId == 2 && l.AppliedTaxRate > 0);
            if (_isLineTaxCalc)
                return internalLines.Sum(l => l.LineTaxAmount);
            // 伝票単位: 税率ごとに合計金額を集計してから floor
            return internalLines
                .GroupBy(l => l.AppliedTaxRate)
                .Sum(g => Math.Floor(g.Sum(l => l.LineAmount) * g.Key / (1 + g.Key)));
        }
    }

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

    // ── フォーカス移動イベント（View コードビハインドがハンドル） ──
    public event Action<string>? FocusField;

    public static class FocusTargets
    {
        public const string LineProductCode     = "LineProductCode";      // 1行目の商品コード
        public const string LineProductCodeLast = "LineProductCodeLast";  // 最終行の商品コード
    }

    // ── コマンド ─────────────────────────────────────────────
    public DelegateCommand NewCommand                  { get; }
    public DelegateCommand SearchCommand               { get; }
    public DelegateCommand PrevSlipCommand             { get; }
    public DelegateCommand NextSlipCommand             { get; }
    public DelegateCommand SaveCommand                 { get; }
    public DelegateCommand PrintCommand                { get; }
    public DelegateCommand DeleteSlipCommand           { get; }
    public DelegateCommand AddLineCommand              { get; }
    public DelegateCommand RemarksEnterCommand         { get; }

    // ルックアップ（Space キー）
    public DelegateCommand OpenCustomerLookupCommand   { get; }
    public DelegateCommand OpenEmployeeLookupCommand   { get; }
    public DelegateCommand OpenOrderLookupCommand      { get; }
    public DelegateCommand OpenSlipLookupCommand       { get; }

    // コード直接補完（Enter キー）
    public DelegateCommand LookupCustomerByCodeCommand { get; }
    public DelegateCommand LookupEmployeeByCodeCommand { get; }
    public DelegateCommand LookupOrderByNoCommand      { get; }

    // ── コンストラクタ ─────────────────────────────────────
    public SalesMainViewModel(ILookupService lookup, ISaleRepository saleRepo, IOrderRepository orderRepo)
    {
        _lookup    = lookup;
        _saleRepo  = saleRepo;
        _orderRepo = orderRepo;

        NewCommand        = new DelegateCommand(OnNew);
        SearchCommand     = new DelegateCommand(async () => await OnSearchAsync());
        PrevSlipCommand   = new DelegateCommand(async () => await OnPrevSlipAsync());
        NextSlipCommand   = new DelegateCommand(async () => await OnNextSlipAsync());
        SaveCommand       = new DelegateCommand(async () => await OnSaveAsync(), () => !IsLocked)
                              .ObservesProperty(() => IsLocked);
        PrintCommand      = new DelegateCommand(OnPrint);
        DeleteSlipCommand = new DelegateCommand(async () => await OnDeleteSlipAsync(), () => !IsLocked)
                              .ObservesProperty(() => IsLocked);
        AddLineCommand = new DelegateCommand(OnAddLine);
        RemarksEnterCommand = new DelegateCommand(OnRemarksEnter);

        OpenCustomerLookupCommand   = new DelegateCommand(OnOpenCustomerLookup);
        OpenEmployeeLookupCommand   = new DelegateCommand(OnOpenEmployeeLookup);
        OpenOrderLookupCommand      = new DelegateCommand(OnOpenOrderLookup);
        OpenSlipLookupCommand       = new DelegateCommand(OnOpenSlipLookup);
        LookupCustomerByCodeCommand = new DelegateCommand(OnLookupCustomerByCode);
        LookupEmployeeByCodeCommand = new DelegateCommand(OnLookupEmployeeByCode);
        LookupOrderByNoCommand      = new DelegateCommand(async () => await OnLookupOrderByNoAsync());

        Lines.CollectionChanged += OnLinesCollectionChanged;

        _ = LoadSlipListAsync();
    }

    // ── 行VM ファクトリ ──────────────────────────────────────
    private SaleLineViewModel CreateLineVm(int lineNo) => new SaleLineViewModel(
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
            foreach (SaleLineViewModel line in e.NewItems)
                line.PropertyChanged += OnLinePropertyChanged;
        if (e.OldItems is not null)
            foreach (SaleLineViewModel line in e.OldItems)
                line.PropertyChanged -= OnLinePropertyChanged;
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SaleLineViewModel.LineAmount)
                           or nameof(SaleLineViewModel.LineTaxAmount)
                           or nameof(SaleLineViewModel.TaxType)
                           or nameof(SaleLineViewModel.LineCostTotal))
            RaiseTotalsChanged();
    }

    // ── 新規 ─────────────────────────────────────────────────
    private void OnNew()
    {
        IsLocked          = false;
        HasSlip           = false;
        _invoicedAt       = null;
        _arAggregatedAt   = null;
        _currentSlipIndex = -1;
        EditSaleNo       = "";
        EditOrderNo      = "";
        EditSaleDate     = DateTime.Today;
        EditCustomerCode = "";
        EditCustomerName = "";
        EditCustomerId   = null;
        EditEmployeeCode = "";
        EditEmployeeName = "";
        EditEmployeeId   = null;
        EditSlipRemarks  = "";
        Lines.Clear();
        RaiseTotalsChanged();
        StatusMessage = "新規伝票";
    }

    private int? EditCustomerId
    {
        get => _editCustomerId;
        set => _editCustomerId = value;
    }

    private int? EditEmployeeId
    {
        get => _editEmployeeId;
        set => _editEmployeeId = value;
    }

    private int? EditOrderId
    {
        get => _editOrderId;
        set => _editOrderId = value;
    }

    // ── 伝票リスト読み込み（ナビ・ルックアップ共用） ──────────
    private List<SlipSummary> _slipSummaries = new();

    private async Task LoadSlipListAsync()
    {
        try
        {
            _slipSummaries = (await _saleRepo.GetSummariesAsync()).ToList();
            _slipNos       = _slipSummaries.Select(s => s.SlipNo).ToList();
            TotalSlipCount = _slipNos.Count;
            if (!string.IsNullOrWhiteSpace(EditSaleNo))
                _currentSlipIndex = _slipNos.IndexOf(EditSaleNo);
        }
        catch { /* ナビ情報取得失敗は無視 */ }
    }

    // ── 外部からの伝票呼び出し（検索画面からのダブルクリック起動）────
    public async Task LoadInitialSlipAsync(string slipNo)
    {
        EditSaleNo = slipNo;
        await OnSearchAsync();
    }

    // ── 伝票検索 ─────────────────────────────────────────────
    private async Task OnSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(EditSaleNo))
        {
            StatusMessage = "伝票No.を入力してください";
            return;
        }
        try
        {
            var slip = await _saleRepo.GetBySlipNoAsync(EditSaleNo.Trim());
            if (slip is null)
            {
                StatusMessage = $"伝票No. '{EditSaleNo}' が見つかりません";
                return;
            }
            LoadSlip(slip);
        }
        catch (Exception ex)
        {
            StatusMessage = $"伝票取得エラー: {ex.Message}";
        }
    }

    private void LoadSlip(SaleSlip slip)
    {
        IsLocked        = slip.IsLocked;
        HasSlip         = true;
        _invoicedAt     = slip.InvoicedAt;
        _arAggregatedAt = slip.ArAggregatedAt;
        RaisePropertyChanged(nameof(InvoicedAtText));
        RaisePropertyChanged(nameof(ArAggregatedAtText));
        // 得意先の税計算単位を解決（CustomerCode でキャッシュから検索）
        var customer = _lookup.FindCustomerByCode(slip.CustomerCode);
        _isLineTaxCalc = customer is not null
            ? ResolveIsLineTaxCalc(customer.TaxCalcUnitId)
            : true;
        _editCustomerPostalCode = slip.CustomerPostalCode ?? "";
        _editCustomerAddress1   = slip.CustomerAddress1   ?? "";
        _editCustomerAddress2   = slip.CustomerAddress2   ?? "";
        EditSaleNo       = slip.SaleNo;
        EditSaleDate     = slip.SaleDate.ToDateTime(TimeOnly.MinValue);
        EditCustomerCode = slip.CustomerCode;
        EditCustomerName = slip.CustomerName;
        _editCustomerId  = slip.CustomerId;
        EditEmployeeCode = slip.EmployeeCode;
        EditEmployeeName = slip.EmployeeName;
        _editEmployeeId  = slip.EmployeeId;
        EditOrderNo      = slip.OrderNo ?? "";
        _editOrderId     = slip.OrderId;
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
        _currentSlipIndex = _slipNos.IndexOf(slip.SaleNo);

        StatusMessage = _isLocked
            ? $"伝票No. {slip.SaleNo}（請求済み・編集不可）"
            : $"伝票No. {slip.SaleNo}";
    }

    // ── ナビゲーション ───────────────────────────────────────
    private async Task OnPrevSlipAsync()
    {
        if (_currentSlipIndex <= 0) return;
        _currentSlipIndex--;
        EditSaleNo = _slipNos[_currentSlipIndex];
        await OnSearchAsync();
    }

    private async Task OnNextSlipAsync()
    {
        if (_currentSlipIndex >= _slipNos.Count - 1) return;
        _currentSlipIndex++;
        EditSaleNo = _slipNos[_currentSlipIndex];
        await OnSearchAsync();
    }

    // ── ルックアップ: 得意先 ─────────────────────────────────
    private void OnOpenCustomerLookup()
    {
        var result = _lookup.OpenCustomerSearch(EditCustomerCode);
        if (result is not null)
            ApplyCustomer(result);
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
        EditCustomerCode          = c.CustomerCode;
        EditCustomerName          = c.CustomerName;
        _editCustomerId           = c.CustomerId;
        _editCustomerPostalCode   = c.PostalCode ?? "";
        _editCustomerAddress1     = c.Address1   ?? "";
        _editCustomerAddress2     = c.Address2   ?? "";
        _isLineTaxCalc            = ResolveIsLineTaxCalc(c.TaxCalcUnitId);
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
    private void ApplyProductToLine(SaleLineViewModel line, Product p)
    {
        line.ProductId   = p.ProductId;
        line.ProductCode = p.ProductCode;
        line.ProductName = p.ProductName;
        line.CostPrice   = p.CostPrice;
        line.TaxRateType = p.TaxRateType;
        var taxType = TaxTypes.FirstOrDefault(t => t.TaxTypeId == p.TaxTypeId);
        line.TaxType = taxType;

        var saleDate = EditSaleDate.HasValue
            ? DateOnly.FromDateTime(EditSaleDate.Value)
            : DateOnly.FromDateTime(DateTime.Today);
        line.AppliedTaxRate = GetAppliedTaxRate(p.TaxRateType, saleDate);

        RaiseTotalsChanged();
        line.RequestMoveToQuantity();
    }

    // ── ルックアップ: 伝票番号 ──────────────────────────────
    private void OnOpenSlipLookup()
    {
        var selected = _lookup.OpenSlipSearch(_slipSummaries, EditSaleNo);
        if (selected is not null)
        {
            EditSaleNo = selected;
            _ = OnSearchAsync();
        }
    }

    // ── ルックアップ: 受注No. ───────────────────────────────
    private async Task OnLookupOrderByNoAsync()
    {
        if (string.IsNullOrWhiteSpace(EditOrderNo)) return;
        try
        {
            var order = await _orderRepo.GetByOrderNoAsync(EditOrderNo.Trim());
            if (order is null)
            {
                StatusMessage = $"受注No. '{EditOrderNo}' が見つかりません";
                return;
            }
            ApplyOrder(order);
        }
        catch (Exception ex)
        {
            StatusMessage = $"受注取得エラー: {ex.Message}";
        }
    }

    private void OnOpenOrderLookup()
    {
        var selected = _lookup.OpenOrderSearch(EditOrderNo);
        if (selected is not null)
        {
            EditOrderNo = selected;
            _ = LoadOrderByNoAsync(selected);
        }
    }

    private async Task LoadOrderByNoAsync(string orderNo)
    {
        try
        {
            var order = await _orderRepo.GetByOrderNoAsync(orderNo);
            if (order is not null) ApplyOrder(order);
        }
        catch (Exception ex)
        {
            StatusMessage = $"受注取得エラー: {ex.Message}";
        }
    }

    private void ApplyOrder(OrderSlip order)
    {
        _editOrderId = order.OrderId;
        EditOrderNo  = order.OrderNo;

        // 得意先: キャッシュから Customer オブジェクトを取得（住所・TaxCalcUnit 含む）
        var customer = _lookup.FindCustomerByCode(order.CustomerCode);
        if (customer is not null)
        {
            ApplyCustomer(customer);
        }
        else
        {
            EditCustomerCode = order.CustomerCode;
            EditCustomerName = order.CustomerName;
            _editCustomerId  = order.CustomerId;
            _isLineTaxCalc   = ResolveIsLineTaxCalc(order.TaxCalcUnitId);
            PropagateLineTaxCalcToLines();
        }

        // 担当者
        var employee = order.EmployeeId != 0 ? _lookup.FindEmployeeById(order.EmployeeId) : null;
        if (employee is not null)
            ApplyEmployee(employee);

        // 摘要
        EditSlipRemarks = order.SlipRemarks ?? "";

        // 明細: 受注内容をそのまま展開（税率は売上日付で再解決）
        Lines.Clear();
        var saleDate = EditSaleDate.HasValue
            ? DateOnly.FromDateTime(EditSaleDate.Value)
            : DateOnly.FromDateTime(DateTime.Today);

        foreach (var l in order.Lines)
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
            vm.AppliedTaxRate = GetAppliedTaxRate(l.TaxRateType, saleDate);
            vm.LineRemarks    = l.LineRemarks ?? "";
            Lines.Add(vm);
        }

        RaiseTotalsChanged();
        StatusMessage = $"受注No. {order.OrderNo} を読み込みました";
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

    private void OnDeleteLine(SaleLineViewModel line)
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
            StatusMessage = "請求済み伝票は編集できません";
            return;
        }
        if (!EditSaleDate.HasValue)
        {
            StatusMessage = "売上日付を入力してください";
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

        var saleDate  = DateOnly.FromDateTime(EditSaleDate.Value);
        var saleNo    = string.IsNullOrWhiteSpace(EditSaleNo)
                            ? GenerateSlipNo(saleDate)
                            : EditSaleNo.Trim();

        var slipTaxTotal = Lines.Sum(l => l.LineTaxAmount);
        var lineInputs = Lines.Select(l => new SaleLineInput(
            l.LineNo, l.ProductId, l.ProductCode, l.ProductName,
            l.Quantity, l.UnitPrice, l.CostPrice,
            l.TaxType?.TaxTypeId ?? 0, l.TaxRateType, l.AppliedTaxRate,
            l.LineTaxAmount, slipTaxTotal,
            string.IsNullOrWhiteSpace(l.LineRemarks) ? null : l.LineRemarks));

        try
        {
            await _saleRepo.UpsertAsync(
                saleNo,
                DateOnly.FromDateTime(EditSaleDate.Value),
                _editCustomerId.Value,
                _editOrderId,
                string.IsNullOrWhiteSpace(EditOrderNo)      ? null : EditOrderNo,
                _editEmployeeId.Value,
                string.IsNullOrWhiteSpace(EditSlipRemarks)  ? null : EditSlipRemarks,
                lineInputs);

            EditSaleNo    = saleNo;
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
        if (string.IsNullOrWhiteSpace(EditSaleNo)) return;
        if (_isLocked)
        {
            StatusMessage = "請求済み伝票は削除できません";
            return;
        }

        var result = MessageBox.Show(
            $"伝票No. {EditSaleNo} を削除しますか？",
            "削除確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _saleRepo.DeleteAsync(EditSaleNo.Trim());
            StatusMessage = "削除しました";
            OnNew();
            await LoadSlipListAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"削除エラー: {ex.Message}";
        }
    }

    // ── 印刷 ─────────────────────────────────────────────────────
    private void OnPrint()
    {
        if (string.IsNullOrWhiteSpace(EditSaleNo) || Lines.Count == 0)
        {
            StatusMessage = "印刷する伝票を読み込んでください";
            return;
        }
        var data = CreatePrintData();
        SalesPrintHelper.Print(data);
    }

    public SalePrintData CreatePrintData()
    {
        return new SalePrintData
        {
            SaleNo               = EditSaleNo,
            SaleDate             = EditSaleDate.HasValue
                ? EditSaleDate.Value.ToString("yyyy年MM月dd日")
                : "",
            CustomerName         = EditCustomerName,
            CustomerPostalCode   = _editCustomerPostalCode,
            CustomerAddress1     = _editCustomerAddress1,
            CustomerAddress2     = _editCustomerAddress2,
            EmployeeName         = EditEmployeeName,
            SlipRemarks  = EditSlipRemarks,
            CompanyName         = _companyInfo.Name,
            CompanyAddress      = _companyInfo.Address,
            CompanyPhone        = _companyInfo.Phone,
            CompanyFax          = _companyInfo.Fax,
            CompanyInvoiceRegNo = _companyInfo.InvoiceRegistrationNo,
            Lines = Lines.Select(l => new SalePrintLine
            {
                LineNo      = l.LineNo,
                ProductCode = l.ProductCode,
                ProductName = l.ProductName,
                Quantity    = l.Quantity.ToString("0.##"),
                UnitPrice   = l.UnitPrice.ToString("N0"),
                LineAmount  = l.LineAmount.ToString("N0"),
                TaxTypeName = l.TaxType?.TaxTypeName ?? "",
                TaxRate     = l.AppliedTaxRateDisplay,
                LineRemarks = l.LineRemarks ?? "",
            }).ToList(),
            TaxBreakdowns       = BuildTaxBreakdowns(),
            TaxExcludedTotalStr = TaxExcludedTotal.ToString("N0"),
            TaxTotalStr         = TaxTotal.ToString("N0"),
            GrandTotalStr       = GrandTotal.ToString("N0"),
        };
    }

    public void SetCompanyInfo(CompanyInfo info) => _companyInfo = info;

    private List<TaxRateBreakdown> BuildTaxBreakdowns()
    {
        var result = new List<TaxRateBreakdown>();

        // 外税（TaxTypeId == 1）税率別
        var externalGroups = Lines
            .Where(l => l.TaxType?.TaxTypeId == 1 && l.AppliedTaxRate > 0)
            .GroupBy(l => l.AppliedTaxRate)
            .OrderByDescending(g => g.Key);

        foreach (var g in externalGroups)
        {
            var baseAmount = g.Sum(l => l.LineAmount);
            var taxAmount  = _isLineTaxCalc
                ? g.Sum(l => l.LineTaxAmount)
                : Math.Floor(baseAmount * g.Key);
            var rateLabel  = g.Key == 0.08m
                ? $"{g.Key:P0}対象（軽減税率）"
                : $"{g.Key:P0}対象";
            result.Add(new TaxRateBreakdown
            {
                Label             = rateLabel,
                TaxExcludedAmount = baseAmount.ToString("N0"),
                TaxAmount         = taxAmount.ToString("N0"),
            });
        }

        // 内税（TaxTypeId == 2）税率別
        var internalGroups = Lines
            .Where(l => l.TaxType?.TaxTypeId == 2 && l.AppliedTaxRate > 0)
            .GroupBy(l => l.AppliedTaxRate)
            .OrderByDescending(g => g.Key);

        foreach (var g in internalGroups)
        {
            var baseAmount = g.Sum(l => l.LineAmount);
            var taxAmount  = _isLineTaxCalc
                ? g.Sum(l => l.LineTaxAmount)
                : Math.Floor(baseAmount * g.Key / (1 + g.Key));
            var rateLabel  = g.Key == 0.08m
                ? $"{g.Key:P0}内税（軽減税率）"
                : $"{g.Key:P0}内税";
            result.Add(new TaxRateBreakdown
            {
                Label             = rateLabel,
                TaxExcludedAmount = baseAmount.ToString("N0"),
                TaxAmount         = taxAmount.ToString("N0"),
            });
        }

        return result;
    }

    // ── 税率期間マスタ ───────────────────────────────────────
    public void SetTaxRatePeriods(IEnumerable<TaxRatePeriod> periods)
        => _taxRatePeriods = periods.ToList();

    // ── 税計算単位 ───────────────────────────────────────────
    /// <summary>1=明細単位, 2=伝票単位（固定）</summary>
    private static bool ResolveIsLineTaxCalc(int taxCalcUnitId) => taxCalcUnitId == 1;

    /// <summary>現在の _isLineTaxCalc をすべての明細行に反映する</summary>
    private void PropagateLineTaxCalcToLines()
    {
        foreach (var line in Lines)
            line.IsLineTaxCalc = _isLineTaxCalc;
        RaiseTotalsChanged();
    }

    /// <summary>売上日付と税率タイプから適用税率を求める。該当なしは 0</summary>
    private decimal GetAppliedTaxRate(byte taxRateType, DateOnly saleDate)
    {
        var period = _taxRatePeriods
            .Where(p => p.StartDate <= saleDate && (p.EndDate is null || p.EndDate >= saleDate))
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefault();
        if (period is null) return 0m;

        return taxRateType switch
        {
            1 => period.PrimaryTaxRate,
            2 => period.SecondaryTaxRate,
            3 => period.TertiaryTaxRate ?? 0m,
            _ => 0m,
        };
    }

    // ── 伝票番号自動生成 ─────────────────────────────────────
    /// <summary>yyyyMMddnnn 形式で当日の連番を生成する</summary>
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
