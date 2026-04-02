using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using bmcs_app.Payment.ViewModels;

namespace bmcs_app.Payment.Views;

public partial class PaymentMainView : MetroWindow
{
    public PaymentMainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PaymentMainViewModel oldVm)
            oldVm.FocusField -= OnFocusField;
        if (e.NewValue is PaymentMainViewModel newVm)
            newVm.FocusField += OnFocusField;
    }

    private void OnFocusField(string target)
    {
        if (target != PaymentMainViewModel.FocusTargets.LinePaymentMethod) return;

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
