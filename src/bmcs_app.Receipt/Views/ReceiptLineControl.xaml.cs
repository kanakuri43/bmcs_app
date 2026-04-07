using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace bmcs_app.Receipt.Views;

public partial class ReceiptLineControl : UserControl
{
    public ReceiptLineControl()
    {
        InitializeComponent();
    }

    /// <summary>外部（ReceiptMainView のコードビハインド）から入金区分欄にフォーカスを当てる</summary>
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
