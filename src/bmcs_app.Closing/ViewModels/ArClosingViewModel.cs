using System.Collections.ObjectModel;
using Prism.Commands;
using Prism.Mvvm;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using bmcs_app.Closing.Services;
using bmcs_app.Infrastructure;

namespace bmcs_app.Closing.ViewModels;

public class ArClosingViewModel : BindableBase
{
    private readonly IClosingRepository _repo;
    private readonly List<Customer>     _customers;
    private CompanyInfo                 _companyInfo = new();

    // ===== 処理日付 =====

    private DateTime? _processDate = EndOfMonth(DateTime.Today);
    public DateTime? ProcessDate
    {
        get => _processDate;
        set => SetProperty(ref _processDate, value);
    }

    // ===== 得意先 =====

    private bool _isAllCustomers = true;
    public bool IsAllCustomers
    {
        get => _isAllCustomers;
        set { SetProperty(ref _isAllCustomers, value); RaisePropertyChanged(nameof(IsSpecificCustomer)); }
    }

    public bool IsSpecificCustomer
    {
        get => !_isAllCustomers;
        set { IsAllCustomers = !value; }
    }

    private string _customerCode = string.Empty;
    public string CustomerCode
    {
        get => _customerCode;
        set => SetProperty(ref _customerCode, value);
    }

    private string _customerName = string.Empty;
    public string CustomerName
    {
        get => _customerName;
        set => SetProperty(ref _customerName, value);
    }

    // ===== 集計履歴 =====

    public ObservableCollection<ArHistorySummary> HistoryItems { get; } = new();

    private ArHistorySummary? _selectedHistoryItem;
    public ArHistorySummary? SelectedHistoryItem
    {
        get => _selectedHistoryItem;
        set
        {
            SetProperty(ref _selectedHistoryItem, value);
            CancelAggregationCommand.RaiseCanExecuteChanged();
            PrintCommand.RaiseCanExecuteChanged();
        }
    }

    // ===== 確認ダイアログ（View がセット） =====

    /// <summary>集計取り消し前の確認。true を返した場合のみ処理を続行する。</summary>
    public Func<string, bool>? ConfirmCancel { get; set; }

    // ===== ステータス =====

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // ===== コマンド =====

    public DelegateCommand AggregateCommand         { get; }
    public DelegateCommand CancelAggregationCommand { get; }
    public DelegateCommand PrintCommand             { get; }

    public ArClosingViewModel(IEnumerable<Customer> customers, IClosingRepository repo)
    {
        _repo      = repo;
        _customers = customers.ToList();

        AggregateCommand         = new DelegateCommand(async () => await OnAggregateAsync());
        CancelAggregationCommand = new DelegateCommand(async () => await OnCancelAggregationAsync(),
                                                       () => SelectedHistoryItem is not null);
        PrintCommand             = new DelegateCommand(async () => await OnPrintAsync(),
                                                       () => SelectedHistoryItem is not null);

        _ = LoadHistoryAsync();
    }

    public void SetCompanyInfo(CompanyInfo info) => _companyInfo = info;

    private static DateTime EndOfMonth(DateTime d)
        => new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

    private async Task LoadHistoryAsync()
    {
        try
        {
            var items = await _repo.GetArHistorySummariesAsync();
            HistoryItems.Clear();
            foreach (var item in items)
                HistoryItems.Add(item);
        }
        catch (Exception ex)
        {
            StatusMessage = $"履歴取得エラー: {ex.Message}";
        }
    }

    private async Task OnAggregateAsync()
    {
        if (ProcessDate is null) { StatusMessage = "処理日付を入力してください。"; return; }

        int? customerId = ResolveCustomerId();
        if (IsSpecificCustomer && customerId is null) { StatusMessage = "得意先コードが見つかりません。"; return; }

        try
        {
            StatusMessage = "売掛金集計中...";
            await _repo.ArClosingAsync(
                DateOnly.FromDateTime(ProcessDate.Value),
                customerId);
            StatusMessage = $"売掛金集計が完了しました。（{ProcessDate.Value:yyyy/MM/dd}）";
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private async Task OnCancelAggregationAsync()
    {
        if (SelectedHistoryItem is null) return;

        var message = $"{SelectedHistoryItem.ClosingDateLabel} の売掛金集計を解除します。よろしいですか？";
        if (ConfirmCancel is not null && !ConfirmCancel(message)) return;

        int? customerId = ResolveCustomerId();
        if (IsSpecificCustomer && customerId is null) { StatusMessage = "得意先コードが見つかりません。"; return; }

        try
        {
            StatusMessage = "集計取り消し中...";
            await _repo.ArClosingCancelAsync(SelectedHistoryItem.ClosingDate, customerId);
            StatusMessage = $"集計取り消しが完了しました。（{SelectedHistoryItem.ClosingDateLabel}）";
            SelectedHistoryItem = null;
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private async Task OnPrintAsync()
    {
        if (SelectedHistoryItem is null) return;

        int? customerId = ResolveCustomerId();
        if (IsSpecificCustomer && customerId is null) { StatusMessage = "得意先コードが見つかりません。"; return; }

        try
        {
            StatusMessage = "印刷データ取得中...";
            var rows = (await _repo.GetArRowsAsync(SelectedHistoryItem.ClosingDate, customerId)).ToList();

            if (rows.Count == 0)
            {
                StatusMessage = "印刷する売掛金データがありません。";
                return;
            }

            StatusMessage = "印刷中...";
            ArBalancePrintHelper.Print(rows, SelectedHistoryItem.ClosingDate, _companyInfo.Name);
            StatusMessage = $"印刷が完了しました。（{SelectedHistoryItem.ClosingDateLabel}  {rows.Count} 件）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private int? ResolveCustomerId()
    {
        if (IsAllCustomers) return null;
        return _customers.FirstOrDefault(c => c.CustomerCode == CustomerCode)?.CustomerId;
    }
}
