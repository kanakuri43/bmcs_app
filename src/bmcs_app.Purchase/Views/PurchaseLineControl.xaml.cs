using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using bmcs_app.Purchase.ViewModels;

namespace bmcs_app.Purchase.Views;

public partial class PurchaseLineControl : UserControl
{
    public PurchaseLineControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PurchaseLineViewModel oldVm)
            oldVm.MoveToQuantityRequested -= OnMoveToQuantity;
        if (e.NewValue is PurchaseLineViewModel newVm)
            newVm.MoveToQuantityRequested += OnMoveToQuantity;
    }

    private void OnMoveToQuantity()
    {
        QuantityBox.Focus();
        QuantityBox.SelectAll();
    }

    /// <summary>外部（PurchaseMainView のコードビハインド）から商品コード欄にフォーカスを当てる</summary>
    public void FocusProductCode()
    {
        ProductCodeBox.Focus();
        ProductCodeBox.SelectAll();
    }

    private void OnTextBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.Text.Length > 0)
            Dispatcher.BeginInvoke(() => tb.SelectAll());
    }
}
