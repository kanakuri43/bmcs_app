using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using bmcs_app.Payment.Services;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Payment.ViewModels;

public class PaymentMainViewModel : BindableBase
{
    private readonly LookupService      _lookup;
    private readonly IPaymentRepository _paymentRepo;
    private bool _isLocked = false;

    // ── 検索・ナビゲーション ─────────────────────────────────────
    private List<string>      _slipNos          = new();
    private int               _currentSlipIndex = -1;
    private List<SlipSummary> _slipSummaries    = new();

    private int _totalSlipCount;
    public int TotalSlipCount
    {
        get => _totalSlipCount;
        set => SetProperty(ref _totalSlipCount, value);
    }

    // ── ヘッダー: 日付・伝票No ─────────────────────────────────
    private DateTime? _editPaymentDate = DateTime.Today;
    public DateTime? EditPaymentDate
    {
        get => _editPaymentDate;
        set => SetProperty(ref _editPaymentDate, value);
    }

    private string _editPaymentNo = "";
    public string EditPaymentNo
    {
        get => _editPaymentNo;
        set => SetProperty(ref _editPaymentNo, value);
    }

    // ── ヘッダー: 仕入先（コード + 名称） ───────────────────────
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

    // ── ヘッダー: 摘要 ───────────────────────────────────────────
    private string _editSlipRemarks = "";
    public string EditSlipRemarks
    {
        get => _editSlipRemarks;
        set => SetProperty(ref _editSlipRemarks, value);
    }

    // ── 支払区分マスタ（UserControl ComboBox 用） ────────────────
    public ObservableCollection<PaymentMethod> PaymentMethods { get; } = new();

    // ── 明細 ────────────────────────────────────────────────────
    public ObservableCollection<PaymentLineViewModel> Lines { get; } = new();

    private PaymentLineViewModel? _selectedLine;
    public PaymentLineViewModel? SelectedLine
    {
        get => _selectedLine;
        set => SetProperty(ref _selectedLine, value);
    }

    // ── 集計 ────────────────────────────────────────────────────
    public decimal GrandTotal => Lines.Sum(l => l.Amount);

    // ── ステータス ───────────────────────────────────────────────
    private string _statusMessage = "準備完了";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // ── フォーカス移動イベント ─────────────────────────────────
    public event Action<string>? FocusField;

    public static class FocusTargets
    {
        public const string LinePaymentMethod = "LinePaymentMethod";
    }

    // ── コマンド ─────────────────────────────────────────────────
    public DelegateCommand NewCommand              { get; }
    public DelegateCommand SearchCommand           { get; }
    public DelegateCommand PrevSlipCommand         { get; }
    public DelegateCommand NextSlipCommand         { get; }
    public DelegateCommand SaveCommand             { get; }
    public DelegateCommand DeleteSlipCommand       { get; }
    public DelegateCommand AddLineCommand          { get; }
    public DelegateCommand DeleteLineCommand       { get; }
    public DelegateCommand RemarksEnterCommand     { get; }

    public DelegateCommand OpenSupplierLookupCommand    { get; }
    public DelegateCommand OpenSlipLookupCommand        { get; }
    public DelegateCommand LookupSupplierByCodeCommand  { get; }

    // ── コンストラクタ ──────────────────────────────────────────
    public PaymentMainViewModel(LookupService lookup, IPaymentRepository paymentRepo)
    {
        _lookup      = lookup;
        _paymentRepo = paymentRepo;

        NewCommand          = new DelegateCommand(OnNew);
        SearchCommand       = new DelegateCommand(async () => await OnSearchAsync());
        PrevSlipCommand     = new DelegateCommand(async () => await OnPrevSlipAsync());
        NextSlipCommand     = new DelegateCommand(async () => await OnNextSlipAsync());
        SaveCommand         = new DelegateCommand(async () => await OnSaveAsync());
        DeleteSlipCommand   = new DelegateCommand(async () => await OnDeleteSlipAsync());
        AddLineCommand      = new DelegateCommand(OnAddLine);
        DeleteLineCommand   = new DelegateCommand(OnDeleteLine, () => SelectedLine is not null)
                                  .ObservesProperty(() => SelectedLine);
        RemarksEnterCommand = new DelegateCommand(OnRemarksEnter);

        OpenSupplierLookupCommand   = new DelegateCommand(OnOpenSupplierLookup);
        OpenSlipLookupCommand       = new DelegateCommand(OnOpenSlipLookup);
        LookupSupplierByCodeCommand = new DelegateCommand(OnLookupSupplierByCode);

        Lines.CollectionChanged += OnLinesCollectionChanged;

        _ = LoadSlipListAsync();
    }

    // ── 行VM のプロパティ変更を購読して合計を再通知 ─────────────
    private void OnLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (PaymentLineViewModel line in e.NewItems)
                line.PropertyChanged += OnLinePropertyChanged;
        if (e.OldItems is not null)
            foreach (PaymentLineViewModel line in e.OldItems)
                line.PropertyChanged -= OnLinePropertyChanged;
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaymentLineViewModel.Amount))
            RaisePropertyChanged(nameof(GrandTotal));
    }

    // ── 新規 ──────────────────────────────────────────────────────
    private void OnNew()
    {
        _isLocked         = false;
        _currentSlipIndex = -1;
        EditPaymentNo     = "";
        EditPaymentDate   = DateTime.Today;
        EditSupplierCode  = "";
        EditSupplierName  = "";
        _editSupplierId   = null;
        EditSlipRemarks   = "";
        Lines.Clear();
        RaisePropertyChanged(nameof(GrandTotal));
        StatusMessage = "新規伝票";
    }

    // ── 伝票リスト読み込み ────────────────────────────────────────
    private async Task LoadSlipListAsync()
    {
        try
        {
            _slipSummaries    = (await _paymentRepo.GetSummariesAsync()).ToList();
            _slipNos          = _slipSummaries.Select(s => s.SlipNo).ToList();
            TotalSlipCount    = _slipNos.Count;
            if (!string.IsNullOrWhiteSpace(EditPaymentNo))
                _currentSlipIndex = _slipNos.IndexOf(EditPaymentNo);
        }
        catch { /* ナビ情報取得失敗は無視 */ }
    }

    // ── 外部からの伝票呼び出し ────────────────────────────────────
    public async Task LoadInitialSlipAsync(string slipNo)
    {
        EditPaymentNo = slipNo;
        await OnSearchAsync();
    }

    // ── 伝票検索 ──────────────────────────────────────────────────
    private async Task OnSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(EditPaymentNo))
        {
            StatusMessage = "伝票No.を入力してください";
            return;
        }
        try
        {
            var slip = await _paymentRepo.GetByPaymentNoAsync(EditPaymentNo.Trim());
            if (slip is null)
            {
                StatusMessage = $"伝票No. '{EditPaymentNo}' が見つかりません";
                return;
            }
            LoadSlip(slip);
        }
        catch (Exception ex)
        {
            StatusMessage = $"伝票取得エラー: {ex.Message}";
        }
    }

    private void LoadSlip(PaymentSlip slip)
    {
        _isLocked        = slip.IsLocked;
        EditPaymentNo    = slip.PaymentNo;
        EditPaymentDate  = slip.PaymentDate.ToDateTime(TimeOnly.MinValue);
        EditSupplierCode = slip.SupplierCode;
        EditSupplierName = slip.SupplierName;
        _editSupplierId  = slip.SupplierId;
        EditSlipRemarks  = slip.SlipRemarks ?? "";

        Lines.Clear();
        foreach (var l in slip.Lines)
        {
            var pm = PaymentMethods.FirstOrDefault(p => p.PaymentMethodId == l.PaymentMethodId);
            var vm = CreateLineVm(l.LineNo);
            vm.PaymentMethod = pm;
            vm.Amount        = l.Amount;
            vm.LineRemarks   = l.LineRemarks ?? "";
            vm.BillDueDate   = l.BillDueDate.HasValue
                                   ? l.BillDueDate.Value.ToDateTime(TimeOnly.MinValue)
                                   : null;
            Lines.Add(vm);
        }

        RaisePropertyChanged(nameof(GrandTotal));
        _currentSlipIndex = _slipNos.IndexOf(slip.PaymentNo);

        StatusMessage = _isLocked
            ? $"伝票No. {slip.PaymentNo}（集計済み・編集不可）"
            : $"伝票No. {slip.PaymentNo}";
    }

    // ── ナビゲーション ─────────────────────────────────────────
    private async Task OnPrevSlipAsync()
    {
        if (_currentSlipIndex <= 0) return;
        _currentSlipIndex--;
        EditPaymentNo = _slipNos[_currentSlipIndex];
        await OnSearchAsync();
    }

    private async Task OnNextSlipAsync()
    {
        if (_currentSlipIndex >= _slipNos.Count - 1) return;
        _currentSlipIndex++;
        EditPaymentNo = _slipNos[_currentSlipIndex];
        await OnSearchAsync();
    }

    // ── ルックアップ: 仕入先 ──────────────────────────────────
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
        EditSupplierCode = s.SupplierCode;
        EditSupplierName = s.SupplierName;
        _editSupplierId  = s.SupplierId;
        StatusMessage    = $"仕入先: {s.SupplierName}";
    }

    // ── ルックアップ: 伝票番号 ────────────────────────────────
    private void OnOpenSlipLookup()
    {
        var selected = _lookup.OpenSlipSearch(EditPaymentNo);
        if (selected is not null)
        {
            EditPaymentNo = selected;
            _ = OnSearchAsync();
        }
    }

    // ── 摘要 Enter ────────────────────────────────────────────
    private void OnRemarksEnter()
    {
        if (Lines.Count == 0)
            OnAddLine();
        FocusField?.Invoke(FocusTargets.LinePaymentMethod);
    }

    // ── 明細行ファクトリ ──────────────────────────────────────
    private PaymentLineViewModel CreateLineVm(int lineNo) => new PaymentLineViewModel(
        onDelete:           vm => OnDeleteLineVm(vm),
        onLineRemarksEnter: vm => { OnAddLine(); FocusField?.Invoke(FocusTargets.LinePaymentMethod); }
    )
    { LineNo = lineNo };

    // ── 明細行操作 ────────────────────────────────────────────
    private void OnAddLine()
    {
        var line = CreateLineVm(Lines.Count + 1);
        Lines.Add(line);
        SelectedLine = line;
        RaisePropertyChanged(nameof(GrandTotal));
    }

    private void OnDeleteLineVm(PaymentLineViewModel vm)
    {
        Lines.Remove(vm);
        for (int i = 0; i < Lines.Count; i++)
            Lines[i].LineNo = i + 1;
        RaisePropertyChanged(nameof(GrandTotal));
        StatusMessage = "行を削除しました";
    }

    private void OnDeleteLine()
    {
        if (SelectedLine is null) return;
        OnDeleteLineVm(SelectedLine);
    }

    // ── 保存 ──────────────────────────────────────────────────
    private async Task OnSaveAsync()
    {
        if (_isLocked)
        {
            StatusMessage = "集計済み伝票は編集できません";
            return;
        }
        if (!EditPaymentDate.HasValue)
        {
            StatusMessage = "支払日付を入力してください";
            return;
        }
        if (_editSupplierId is null)
        {
            StatusMessage = "仕入先を指定してください";
            return;
        }
        if (Lines.Count == 0)
        {
            StatusMessage = "明細行を1件以上入力してください";
            return;
        }
        if (Lines.Any(l => l.PaymentMethod is null))
        {
            StatusMessage = "支払区分が未設定の行があります";
            return;
        }
        if (Lines.Any(l => l.Amount == 0))
        {
            StatusMessage = "金額が0の行があります";
            return;
        }

        var paymentDate = DateOnly.FromDateTime(EditPaymentDate.Value);
        var paymentNo   = string.IsNullOrWhiteSpace(EditPaymentNo)
                              ? GenerateSlipNo(paymentDate)
                              : EditPaymentNo.Trim();

        var lineInputs = Lines.Select(l => new PaymentLineInput(
            l.LineNo,
            l.PaymentMethod!.PaymentMethodId,
            l.Amount,
            string.IsNullOrWhiteSpace(l.LineRemarks) ? null : l.LineRemarks,
            l.BillDueDate.HasValue ? DateOnly.FromDateTime(l.BillDueDate.Value) : null));

        try
        {
            await _paymentRepo.UpsertAsync(
                paymentNo,
                paymentDate,
                _editSupplierId.Value,
                string.IsNullOrWhiteSpace(EditSlipRemarks) ? null : EditSlipRemarks,
                lineInputs);

            EditPaymentNo = paymentNo;
            StatusMessage = "登録しました";
            await LoadSlipListAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
        }
    }

    // ── 削除 ──────────────────────────────────────────────────
    private async Task OnDeleteSlipAsync()
    {
        if (string.IsNullOrWhiteSpace(EditPaymentNo)) return;
        if (_isLocked)
        {
            StatusMessage = "集計済み伝票は削除できません";
            return;
        }

        var result = MessageBox.Show(
            $"伝票No. {EditPaymentNo} を削除しますか？",
            "削除確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _paymentRepo.DeleteAsync(EditPaymentNo.Trim());
            StatusMessage = "削除しました";
            OnNew();
            await LoadSlipListAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"削除エラー: {ex.Message}";
        }
    }

    // ── 伝票番号自動生成 ───────────────────────────────────────
    private string GenerateSlipNo(DateOnly date)
    {
        var prefix = date.ToString("yyyyMMdd");
        var count  = _slipNos.Count(n => n.StartsWith(prefix));
        return $"{prefix}{count + 1:000}";
    }
}
