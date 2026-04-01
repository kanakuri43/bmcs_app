using System.Collections.ObjectModel;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Sales.ViewModels;

public class SalesMainViewModel : BindableBase
{
    private readonly ILookupService  _lookup;
    private readonly ISaleRepository _saleRepo;

    // ── 検索・ナビゲーション ─────────────────────────────────
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

    private SaleLineViewModel? _selectedLine;
    public SaleLineViewModel? SelectedLine
    {
        get => _selectedLine;
        set => SetProperty(ref _selectedLine, value);
    }

    // ── 集計（明細変更のたびに再計算） ──────────────────────
    public decimal TaxExcludedTotal  => Lines.Sum(l => l.LineAmount);
    public decimal ExternalTaxTotal  => Lines.Where(l => l.TaxType?.TaxTypeId == 1).Sum(l => l.LineTaxAmount);
    public decimal InternalTaxTotal  => Lines.Where(l => l.TaxType?.TaxTypeId == 2).Sum(l => l.LineTaxAmount);
    public decimal TaxTotal          => ExternalTaxTotal + InternalTaxTotal;
    public decimal GrandTotal        => TaxExcludedTotal + ExternalTaxTotal;

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
        public const string LineProductCode = "LineProductCode";
    }

    // ── コマンド ─────────────────────────────────────────────
    public DelegateCommand NewCommand                  { get; }
    public DelegateCommand SearchCommand               { get; }
    public DelegateCommand PrevSlipCommand             { get; }
    public DelegateCommand NextSlipCommand             { get; }
    public DelegateCommand SaveCommand                 { get; }
    public DelegateCommand DeleteSlipCommand           { get; }
    public DelegateCommand AddLineCommand              { get; }
    public DelegateCommand DeleteLineCommand           { get; }
    public DelegateCommand RemarksEnterCommand         { get; }

    // ルックアップ（Space キー）
    public DelegateCommand OpenCustomerLookupCommand   { get; }
    public DelegateCommand OpenEmployeeLookupCommand   { get; }
    public DelegateCommand OpenProductLookupCommand    { get; }
    public DelegateCommand OpenOrderLookupCommand      { get; }
    public DelegateCommand OpenSlipLookupCommand       { get; }

    // コード直接補完（Enter キー）
    public DelegateCommand LookupCustomerByCodeCommand { get; }
    public DelegateCommand LookupEmployeeByCodeCommand { get; }
    public DelegateCommand LookupOrderByNoCommand      { get; }
    public DelegateCommand LookupProductByCodeCommand  { get; }

    // ── コンストラクタ ─────────────────────────────────────
    public SalesMainViewModel(ILookupService lookup, ISaleRepository saleRepo)
    {
        _lookup   = lookup;
        _saleRepo = saleRepo;

        NewCommand        = new DelegateCommand(OnNew);
        SearchCommand     = new DelegateCommand(OnSearch);
        PrevSlipCommand   = new DelegateCommand(OnPrevSlip);
        NextSlipCommand   = new DelegateCommand(OnNextSlip);
        SaveCommand       = new DelegateCommand(async () => await OnSaveAsync());
        DeleteSlipCommand = new DelegateCommand(async () => await OnDeleteSlipAsync());
        AddLineCommand        = new DelegateCommand(OnAddLine);
        DeleteLineCommand     = new DelegateCommand(OnDeleteLine, () => SelectedLine is not null)
                                    .ObservesProperty(() => SelectedLine);
        RemarksEnterCommand   = new DelegateCommand(OnRemarksEnter);

        OpenCustomerLookupCommand   = new DelegateCommand(OnOpenCustomerLookup);
        OpenEmployeeLookupCommand   = new DelegateCommand(OnOpenEmployeeLookup);
        OpenProductLookupCommand    = new DelegateCommand(OnOpenProductLookup);
        OpenOrderLookupCommand      = new DelegateCommand(OnOpenOrderLookup);
        OpenSlipLookupCommand       = new DelegateCommand(async () => await OnOpenSlipLookupAsync());
        LookupCustomerByCodeCommand = new DelegateCommand(OnLookupCustomerByCode);
        LookupEmployeeByCodeCommand = new DelegateCommand(OnLookupEmployeeByCode);
        LookupOrderByNoCommand      = new DelegateCommand(OnLookupOrderByNo);
        LookupProductByCodeCommand  = new DelegateCommand(OnLookupProductByCode);
    }

    // ── 新規 ─────────────────────────────────────────────────
    private void OnNew()
    {
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

    // ── 検索・ナビ（TODO） ───────────────────────────────────
    private void OnSearch()   { /* TODO */ }
    private void OnPrevSlip() { /* TODO */ }
    private void OnNextSlip() { /* TODO */ }

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
        EditCustomerCode = c.CustomerCode;
        EditCustomerName = c.CustomerName;
        _editCustomerId  = c.CustomerId;
        StatusMessage    = $"得意先: {c.CustomerName}";
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

    // ── ルックアップ: 商品（明細行） ────────────────────────
    private void OnOpenProductLookup()
    {
        if (SelectedLine is null) return;
        var result = _lookup.OpenProductSearch(SelectedLine.ProductCode);
        if (result is not null)
            ApplyProductToLine(SelectedLine, result);
    }

    private void ApplyProductToLine(SaleLineViewModel line, Product p)
    {
        line.ProductId   = p.ProductId;
        line.ProductCode = p.ProductCode;
        line.ProductName = p.ProductName;
        var taxType = TaxTypes.FirstOrDefault(t => t.TaxTypeId == p.TaxTypeId);
        line.TaxType = taxType;
        RaiseTotalsChanged();
    }

    // ── ルックアップ: 伝票番号 ──────────────────────────────
    private async Task OnOpenSlipLookupAsync()
    {
        try
        {
            var summaries = await _saleRepo.GetSummariesAsync();
            var selected  = _lookup.OpenSlipSearch(summaries, EditSaleNo);
            if (selected is not null)
            {
                EditSaleNo = selected;
                OnSearch();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"伝票一覧の取得エラー: {ex.Message}";
        }
    }

    // ── ルックアップ: 商品コード Enter（明細行） ─────────────
    private void OnLookupProductByCode()
    {
        if (SelectedLine is null) return;
        var result = _lookup.FindProductByCode(SelectedLine.ProductCode);
        if (result is not null)
            ApplyProductToLine(SelectedLine, result);
        else if (!string.IsNullOrWhiteSpace(SelectedLine.ProductCode))
            StatusMessage = $"商品コード '{SelectedLine.ProductCode}' が見つかりません";
    }

    // ── ルックアップ: 受注No. ───────────────────────────────
    private void OnLookupOrderByNo()
    {
        // 受注No. 入力後 Enter → Tab 順で次フィールド（得意先コード）へ
    }

    private void OnOpenOrderLookup() { /* TODO: 受注選択ダイアログ */ }

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
        var line = new SaleLineViewModel { LineNo = Lines.Count + 1 };
        Lines.Add(line);
        SelectedLine = line;
        RaiseTotalsChanged();
    }

    private void OnDeleteLine()
    {
        if (SelectedLine is null) return;
        Lines.Remove(SelectedLine);
        for (int i = 0; i < Lines.Count; i++)
            Lines[i].LineNo = i + 1;
        RaiseTotalsChanged();
        StatusMessage = "行を削除しました";
    }

    // ── 保存・削除（TODO） ───────────────────────────────────
    private Task OnSaveAsync()       => Task.CompletedTask;
    private Task OnDeleteSlipAsync() => Task.CompletedTask;

    // ── 集計再通知 ───────────────────────────────────────────
    public void RaiseTotalsChanged()
    {
        RaisePropertyChanged(nameof(TaxExcludedTotal));
        RaisePropertyChanged(nameof(ExternalTaxTotal));
        RaisePropertyChanged(nameof(InternalTaxTotal));
        RaisePropertyChanged(nameof(TaxTotal));
        RaisePropertyChanged(nameof(GrandTotal));
    }
}
