using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using bmcs_app.Sales.ViewModels;

namespace bmcs_app.Sales.Views;

public partial class SalesMainView : MetroWindow
{
    public SalesMainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SalesMainViewModel oldVm)
            oldVm.FocusField -= OnFocusField;
        if (e.NewValue is SalesMainViewModel newVm)
            newVm.FocusField += OnFocusField;
    }

    private void OnFocusField(string target)
    {
        if (target != SalesMainViewModel.FocusTargets.LineProductCode) return;

        Dispatcher.BeginInvoke(() =>
        {
            if (LinesGrid.Items.Count == 0) return;
            var row = LinesGrid.Items[0];
            LinesGrid.SelectedItem = row;
            LinesGrid.ScrollIntoView(row);
            // 商品コード列 (index=1)
            LinesGrid.CurrentCell = new DataGridCellInfo(row, LinesGrid.Columns[1]);
            LinesGrid.BeginEdit();
        }, DispatcherPriority.Input);
    }
}
