using bmcs_app.Core.Models;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.Payment.ViewModels;

/// <summary>支払明細の1行分（UserControl で表示）</summary>
public class PaymentLineViewModel : BindableBase
{
    private readonly Action<PaymentLineViewModel> _onDelete;
    private readonly Action<PaymentLineViewModel> _onLineRemarksEnter;

    private int            _lineNo;
    private PaymentMethod? _paymentMethod;
    private decimal        _amount;
    private string         _lineRemarks  = "";
    private DateTime?      _billDueDate;

    public DelegateCommand DeleteCommand           { get; }
    public DelegateCommand LineRemarksEnterCommand { get; }

    public PaymentLineViewModel(
        Action<PaymentLineViewModel> onDelete,
        Action<PaymentLineViewModel> onLineRemarksEnter)
    {
        _onDelete            = onDelete;
        _onLineRemarksEnter  = onLineRemarksEnter;

        DeleteCommand           = new DelegateCommand(() => _onDelete(this));
        LineRemarksEnterCommand = new DelegateCommand(() => _onLineRemarksEnter(this));
    }

    public int LineNo
    {
        get => _lineNo;
        set => SetProperty(ref _lineNo, value);
    }

    public PaymentMethod? PaymentMethod
    {
        get => _paymentMethod;
        set
        {
            if (SetProperty(ref _paymentMethod, value))
            {
                RaisePropertyChanged(nameof(PaymentMethodName));
                RaisePropertyChanged(nameof(IsBillDueDateVisible));
            }
        }
    }

    /// <summary>表示用</summary>
    public string PaymentMethodName => _paymentMethod?.PaymentMethodName ?? "";

    /// <summary>手形の場合のみ手形期日欄を表示</summary>
    public bool IsBillDueDateVisible => _paymentMethod?.PaymentMethodName == "手形";

    public decimal Amount
    {
        get => _amount;
        set => SetProperty(ref _amount, value);
    }

    public string LineRemarks
    {
        get => _lineRemarks;
        set => SetProperty(ref _lineRemarks, value);
    }

    public DateTime? BillDueDate
    {
        get => _billDueDate;
        set => SetProperty(ref _billDueDate, value);
    }
}
