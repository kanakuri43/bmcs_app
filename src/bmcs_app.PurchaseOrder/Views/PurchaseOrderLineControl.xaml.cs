using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using bmcs_app.PurchaseOrder.ViewModels;

namespace bmcs_app.PurchaseOrder.Views;

public partial class PurchaseOrderLineControl : UserControl
{
    public PurchaseOrderLineControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PurchaseOrderLineViewModel oldVm)
            oldVm.MoveToQuantityRequested -= OnMoveToQuantity;
        if (e.NewValue is PurchaseOrderLineViewModel newVm)
            newVm.MoveToQuantityRequested += OnMoveToQuantity;
    }

    private void OnMoveToQuantity()
    {
        QuantityBox.Focus();
        QuantityBox.SelectAll();
    }

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
