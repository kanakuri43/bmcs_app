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
    private readonly IReceiptRepository _receiptRepo;
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
    private DateTime? _editReceiptDate = DateTime.Today;
    public DateTime? EditReceiptDate
    {
        get => _editReceiptDate;
        set => SetProperty(ref _editReceiptDate, value);
    }

    private string _editReceiptNo = "";
    public string EditReceiptNo
    {
        get => _editReceiptNo;
        set => SetProperty(ref _editReceiptNo, value);
    }

    // ── ヘッダー: 得意先（コード + 名称） ───────────────────────
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

    // ── ヘッダー: 摘要 ───────────────────────────────────────────
    private string _editSlipRemarks = "";
    public string EditSlipRemarks
    {
        get => _editSlipRemarks;
        set => SetProperty(ref _editSlipRemarks, value);
    }

    // ── 入金区分マスタ（DataGrid ComboBox 用） ───────────────────
    public ObservableCollection<PaymentMethod> PaymentMethods { get; } = new();

    // ── 明細 ────────────────────────────────────────────────────
    public ObservableCollection<ReceiptLineViewModel> Lines { get; } = new();

    private ReceiptLineViewModel? _selectedLine;
    public ReceiptLineViewModel? SelectedLine
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

    public DelegateCommand OpenCustomerLookupCommand    { get; }
    public DelegateCommand OpenSlipLookupCommand        { get; }
    public DelegateCommand LookupCustomerByCodeCommand  { get; }

    // ── コンストラクタ ──────────────────────────────────────────
    public PaymentMainViewModel(LookupService lookup, IReceiptRepository receiptRepo)
    {
        _lookup      = lookup;
        _receiptRepo = receiptRepo;

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

        OpenCustomerLookupCommand   = new DelegateCommand(OnOpenCustomerLookup);
        OpenSlipLookupCommand       = new DelegateCommand(OnOpenSlipLookup);
        LookupCustomerByCodeCommand = new DelegateCommand(OnLookupCustomerByCode);

        Lines.CollectionChanged += OnLinesCollectionChanged;

        _ = LoadSlipListAsync();
    }

    // ── 行VM のプロパティ変更を購読して合計を再通知 ─────────────
    private void OnLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (ReceiptLineViewModel line in e.NewItems)
                line.PropertyChanged += OnLinePropertyChanged;
        if (e.OldItems is not null)
            foreach (ReceiptLineViewModel line in e.OldItems)
                line.PropertyChanged -= OnLinePropertyChanged;
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReceiptLineViewModel.Amount))
            RaisePropertyChanged(nameof(GrandTotal));
    }

    // ── 新規 ──────────────────────────────────────────────────────
    private void OnNew()
    {
        _isLocked         = false;
        _currentSlipIndex = -1;
        EditReceiptNo     = "";
        EditReceiptDate   = DateTime.Today;
        EditCustomerCode  = "";
        EditCustomerName  = "";
        _editCustomerId   = null;
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
            _slipSummaries    = (await _receiptRepo.GetSummariesAsync()).ToList();
            _slipNos          = _slipSummaries.Select(s => s.SlipNo).ToList();
            TotalSlipCount    = _slipNos.Count;
            if (!string.IsNullOrWhiteSpace(EditReceiptNo))
                _currentSlipIndex = _slipNos.IndexOf(EditReceiptNo);
        }
        catch { /* ナビ情報取得失敗は無視 */ }
    }

    // ── 伝票検索 ──────────────────────────────────────────────────
    private async Task OnSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(EditReceiptNo))
        {
            StatusMessage = "伝票No.を入力してください";
            return;
        }
        try
        {
            var slip = await _receiptRepo.GetByReceiptNoAsync(EditReceiptNo.Trim());
            if (slip is null)
            {
                StatusMessage = $"伝票No. '{EditReceiptNo}' が見つかりません";
                return;
            }
            LoadSlip(slip);
        }
        catch (Exception ex)
        {
            StatusMessage = $"伝票取得エラー: {ex.Message}";
        }
    }

    private void LoadSlip(ReceiptSlip slip)
    {
        _isLocked        = slip.IsLocked;
        EditReceiptNo    = slip.ReceiptNo;
        EditReceiptDate  = slip.ReceiptDate.ToDateTime(TimeOnly.MinValue);
        EditCustomerCode = slip.CustomerCode;
        EditCustomerName = slip.CustomerName;
        _editCustomerId  = slip.CustomerId;
        EditSlipRemarks  = slip.SlipRemarks ?? "";

        Lines.Clear();
        foreach (var l in slip.Lines)
        {
            var pm = PaymentMethods.FirstOrDefault(p => p.PaymentMethodId == l.PaymentMethodId);
            Lines.Add(new ReceiptLineViewModel
            {
                LineNo        = l.LineNo,
                PaymentMethod = pm,
                Amount        = l.Amount,
                LineRemarks   = l.LineRemarks ?? "",
            });
        }

        RaisePropertyChanged(nameof(GrandTotal));
        _currentSlipIndex = _slipNos.IndexOf(slip.ReceiptNo);

        StatusMessage = _isLocked
            ? $"伝票No. {slip.ReceiptNo}（集計済み・編集不可）"
            : $"伝票No. {slip.ReceiptNo}";
    }

    // ── ナビゲーション ─────────────────────────────────────────
    private async Task OnPrevSlipAsync()
    {
        if (_currentSlipIndex <= 0) return;
        _currentSlipIndex--;
        EditReceiptNo = _slipNos[_currentSlipIndex];
        await OnSearchAsync();
    }

    private async Task OnNextSlipAsync()
    {
        if (_currentSlipIndex >= _slipNos.Count - 1) return;
        _currentSlipIndex++;
        EditReceiptNo = _slipNos[_currentSlipIndex];
        await OnSearchAsync();
    }

    // ── ルックアップ: 得意先 ──────────────────────────────────
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

    // ── ルックアップ: 伝票番号 ────────────────────────────────
    private void OnOpenSlipLookup()
    {
        var selected = _lookup.OpenSlipSearch(_slipSummaries, EditReceiptNo);
        if (selected is not null)
        {
            EditReceiptNo = selected;
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

    // ── 明細行操作 ────────────────────────────────────────────
    private void OnAddLine()
    {
        var line = new ReceiptLineViewModel { LineNo = Lines.Count + 1 };
        Lines.Add(line);
        SelectedLine = line;
        RaisePropertyChanged(nameof(GrandTotal));
    }

    private void OnDeleteLine()
    {
        if (SelectedLine is null) return;
        Lines.Remove(SelectedLine);
        for (int i = 0; i < Lines.Count; i++)
            Lines[i].LineNo = i + 1;
        RaisePropertyChanged(nameof(GrandTotal));
        StatusMessage = "行を削除しました";
    }

    // ── 保存 ──────────────────────────────────────────────────
    private async Task OnSaveAsync()
    {
        if (_isLocked)
        {
            StatusMessage = "集計済み伝票は編集できません";
            return;
        }
        if (!EditReceiptDate.HasValue)
        {
            StatusMessage = "入金日付を入力してください";
            return;
        }
        if (_editCustomerId is null)
        {
            StatusMessage = "得意先を指定してください";
            return;
        }
        if (Lines.Count == 0)
        {
            StatusMessage = "明細行を1件以上入力してください";
            return;
        }
        if (Lines.Any(l => l.PaymentMethod is null))
        {
            StatusMessage = "入金区分が未設定の行があります";
            return;
        }
        if (Lines.Any(l => l.Amount == 0))
        {
            StatusMessage = "金額が0の行があります";
            return;
        }

        var receiptDate = DateOnly.FromDateTime(EditReceiptDate.Value);
        var receiptNo   = string.IsNullOrWhiteSpace(EditReceiptNo)
                              ? GenerateSlipNo(receiptDate)
                              : EditReceiptNo.Trim();

        var lineInputs = Lines.Select(l => new ReceiptLineInput(
            l.LineNo,
            l.PaymentMethod!.PaymentMethodId,
            l.Amount,
            string.IsNullOrWhiteSpace(l.LineRemarks) ? null : l.LineRemarks));

        try
        {
            await _receiptRepo.UpsertAsync(
                receiptNo,
                receiptDate,
                _editCustomerId.Value,
                string.IsNullOrWhiteSpace(EditSlipRemarks) ? null : EditSlipRemarks,
                lineInputs);

            EditReceiptNo = receiptNo;
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
        if (string.IsNullOrWhiteSpace(EditReceiptNo)) return;
        if (_isLocked)
        {
            StatusMessage = "集計済み伝票は削除できません";
            return;
        }

        var result = MessageBox.Show(
            $"伝票No. {EditReceiptNo} を削除しますか？",
            "削除確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _receiptRepo.DeleteAsync(EditReceiptNo.Trim());
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
