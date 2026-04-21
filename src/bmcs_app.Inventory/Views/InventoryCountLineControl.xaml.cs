using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using bmcs_app.Inventory.ViewModels;

namespace bmcs_app.Inventory.Views;

public partial class InventoryCountLineControl : UserControl
{
    public InventoryCountLineControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is InventoryCountLineViewModel oldVm)
            oldVm.MoveToQuantityRequested -= OnMoveToQuantity;
        if (e.NewValue is InventoryCountLineViewModel newVm)
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
