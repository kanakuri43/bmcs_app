using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using bmcs_app.Order.ViewModels;

namespace bmcs_app.Order.Views;

public partial class OrderLineControl : UserControl
{
    public OrderLineControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is OrderLineViewModel oldVm)
            oldVm.MoveToQuantityRequested -= OnMoveToQuantity;
        if (e.NewValue is OrderLineViewModel newVm)
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
