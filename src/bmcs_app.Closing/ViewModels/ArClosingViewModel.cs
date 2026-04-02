using Prism.Commands;
using Prism.Mvvm;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;

namespace bmcs_app.Closing.ViewModels;

public class ArClosingViewModel : BindableBase
{
    private readonly IClosingRepository _repo;
    private readonly List<Customer>     _customers;

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

    public ArClosingViewModel(IEnumerable<Customer> customers, IClosingRepository repo)
    {
        _repo      = repo;
        _customers = customers.ToList();

        AggregateCommand         = new DelegateCommand(async () => await OnAggregateAsync());
        CancelAggregationCommand = new DelegateCommand(async () => await OnCancelAggregationAsync());
    }

    private static DateTime EndOfMonth(DateTime d)
        => new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

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
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private async Task OnCancelAggregationAsync()
    {
        if (ProcessDate is null) { StatusMessage = "処理日付を入力してください。"; return; }

        int? customerId = ResolveCustomerId();
        if (IsSpecificCustomer && customerId is null) { StatusMessage = "得意先コードが見つかりません。"; return; }

        try
        {
            StatusMessage = "集計取り消し中...";
            await _repo.ArClosingCancelAsync(
                DateOnly.FromDateTime(ProcessDate.Value),
                customerId);
            StatusMessage = $"集計取り消しが完了しました。（{ProcessDate.Value:yyyy/MM/dd}）";
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
