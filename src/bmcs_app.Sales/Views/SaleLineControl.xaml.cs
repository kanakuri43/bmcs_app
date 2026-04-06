using System.Windows;
using System.Windows.Controls;
using bmcs_app.Sales.ViewModels;

namespace bmcs_app.Sales.Views;

public partial class SaleLineControl : UserControl
{
    public SaleLineControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SaleLineViewModel oldVm)
            oldVm.MoveToQuantityRequested -= OnMoveToQuantity;
        if (e.NewValue is SaleLineViewModel newVm)
            newVm.MoveToQuantityRequested += OnMoveToQuantity;
    }

    private void OnMoveToQuantity()
    {
        QuantityBox.Focus();
        QuantityBox.SelectAll();
    }

    /// <summary>外部（SalesMainView のコードビハインド）から商品コード欄にフォーカスを当てる</summary>
    public void FocusProductCode()
    {
        ProductCodeBox.Focus();
        ProductCodeBox.SelectAll();
    }
}
