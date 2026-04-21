using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using bmcs_app.Inventory.ViewModels;

namespace bmcs_app.Inventory.Views;

public partial class InventoryCountView : MetroWindow
{
    public InventoryCountView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is InventoryCountViewModel oldVm)
            oldVm.FocusField -= OnFocusField;
        if (e.NewValue is InventoryCountViewModel newVm)
            newVm.FocusField += OnFocusField;
    }

    private void OnFocusField(string target)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var controls = FindVisualChildren<InventoryCountLineControl>(LinesContainer).ToList();
            var ctrl = target == InventoryCountViewModel.FocusTargets.LineProductCodeLast
                ? controls.LastOrDefault()
                : null;
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
