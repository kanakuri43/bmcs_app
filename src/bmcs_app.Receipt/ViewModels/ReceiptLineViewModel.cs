using bmcs_app.Core.Models;
using Prism.Mvvm;

namespace bmcs_app.Receipt.ViewModels;

/// <summary>入金明細 DataGrid の1行分（編集可能）</summary>
public class ReceiptLineViewModel : BindableBase
{
    private int            _lineNo;
    private PaymentMethod? _paymentMethod;
    private decimal        _amount;
    private string         _lineRemarks = "";

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
                RaisePropertyChanged(nameof(PaymentMethodName));
        }
    }

    /// <summary>表示用（ComboBox の DisplayMember 等で使用）</summary>
    public string PaymentMethodName => _paymentMethod?.PaymentMethodName ?? "";

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
}
