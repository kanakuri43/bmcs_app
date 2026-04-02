using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using bmcs_app.Receipt.ViewModels;

namespace bmcs_app.Receipt.Views;

public partial class ReceiptMainView : MetroWindow
{
    public ReceiptMainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ReceiptMainViewModel oldVm)
            oldVm.FocusField -= OnFocusField;
        if (e.NewValue is ReceiptMainViewModel newVm)
            newVm.FocusField += OnFocusField;
    }

    private void OnFocusField(string target)
    {
        if (target != ReceiptMainViewModel.FocusTargets.LinePaymentMethod) return;

        Dispatcher.BeginInvoke(() =>
        {
            if (LinesGrid.Items.Count == 0) return;
            var row = LinesGrid.Items[LinesGrid.Items.Count - 1];
            LinesGrid.SelectedItem = row;
            LinesGrid.ScrollIntoView(row);
            // 入金区分列 (index=1)
            LinesGrid.CurrentCell = new DataGridCellInfo(row, LinesGrid.Columns[1]);
            LinesGrid.BeginEdit();
        }, DispatcherPriority.Input);
    }
}
