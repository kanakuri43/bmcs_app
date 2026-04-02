using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Closing.ViewModels;

public record ClosingDayOption(byte Day)
{
    public string Label => Day is 0 or 99 ? "末日" : $"{Day}日";
}

/// <summary>請求処理タブ</summary>
public class InvoiceClosingViewModel : BindableBase
{
    // ===== 締め日 =====

    public List<ClosingDayOption> ClosingDays { get; } = new();

    private ClosingDayOption? _selectedClosingDay;
    public ClosingDayOption? SelectedClosingDay
    {
        get => _selectedClosingDay;
        set => SetProperty(ref _selectedClosingDay, value);
    }

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
    public DelegateCommand PrintCommand             { get; }

    public InvoiceClosingViewModel(IEnumerable<byte> closingDays)
    {
        foreach (var d in closingDays.Distinct().OrderBy(d => d))
            ClosingDays.Add(new ClosingDayOption(d));

        SelectedClosingDay = ClosingDays.FirstOrDefault();

        AggregateCommand         = new DelegateCommand(OnAggregate);
        CancelAggregationCommand = new DelegateCommand(OnCancelAggregation);
        PrintCommand             = new DelegateCommand(OnPrint);
    }

    private static DateTime EndOfMonth(DateTime d)
        => new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

    private void OnAggregate()         { /* TODO */ }
    private void OnCancelAggregation() { /* TODO */ }
    private void OnPrint()             { /* TODO */ }
}
