using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace bmcs_app.Payment.Views;

public partial class PaymentLineControl : UserControl
{
    public PaymentLineControl()
    {
        InitializeComponent();
    }

    /// <summary>外部（PaymentMainView のコードビハインド）から支払区分欄にフォーカスを当てる</summary>
    public void FocusPaymentMethod()
    {
        PaymentMethodBox.Focus();
    }

    private void OnTextBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.Text.Length > 0)
            Dispatcher.BeginInvoke(() => tb.SelectAll());
    }
}
