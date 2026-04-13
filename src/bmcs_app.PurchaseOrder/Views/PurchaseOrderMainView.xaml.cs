using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using bmcs_app.PurchaseOrder.ViewModels;

namespace bmcs_app.PurchaseOrder.Views;

public partial class PurchaseOrderMainView : MetroWindow
{
    public PurchaseOrderMainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PurchaseOrderMainViewModel oldVm)
            oldVm.FocusField -= OnFocusField;
        if (e.NewValue is PurchaseOrderMainViewModel newVm)
            newVm.FocusField += OnFocusField;
    }

    private void OnFocusField(string target)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var controls = FindVisualChildren<PurchaseOrderLineControl>(LinesContainer).ToList();
            PurchaseOrderLineControl? ctrl = target switch
            {
                PurchaseOrderMainViewModel.FocusTargets.LineProductCode     => controls.FirstOrDefault(),
                PurchaseOrderMainViewModel.FocusTargets.LineProductCodeLast => controls.LastOrDefault(),
                _ => null,
            };
            ctrl?.FocusProductCode();
        }, DispatcherPriority.Input);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) yield return result;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
