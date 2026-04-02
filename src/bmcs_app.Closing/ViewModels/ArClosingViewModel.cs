using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Closing.ViewModels;

/// <summary>売掛金集計タブ</summary>
public class ArClosingViewModel : BindableBase
{
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

    public ArClosingViewModel()
    {
        AggregateCommand         = new DelegateCommand(OnAggregate);
        CancelAggregationCommand = new DelegateCommand(OnCancelAggregation);
    }

    private static DateTime EndOfMonth(DateTime d)
        => new DateTime(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

    private void OnAggregate()         { /* TODO */ }
    private void OnCancelAggregation() { /* TODO */ }
}
