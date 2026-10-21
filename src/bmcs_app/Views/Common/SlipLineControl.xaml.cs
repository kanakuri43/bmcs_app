using System.Windows.Controls;
using System.Windows.Input;
using bmcs_app.ViewModels.Common;

namespace bmcs_app.Views.Common;

/// <summary>伝票明細行の1行分。</summary>
public partial class SlipLineControl : UserControl
{
    public SlipLineControl()
    {
        InitializeComponent();

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is SlipLineViewModel oldLine)
            {
                oldLine.MoveToQuantityRequested -= OnMoveToQuantityRequested;
            }

            if (e.NewValue is SlipLineViewModel newLine)
            {
                newLine.MoveToQuantityRequested += OnMoveToQuantityRequested;
            }
        };
    }

    private void OnMoveToQuantityRequested() => QuantityBox.Focus();

    private string _productNameBefore = string.Empty;

    private void ProductNameBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        _productNameBefore = ((TextBox)sender).Text;

    private void ProductNameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        (DataContext as SlipLineViewModel)?.CommitProductNameEdit(_productNameBefore);
}
