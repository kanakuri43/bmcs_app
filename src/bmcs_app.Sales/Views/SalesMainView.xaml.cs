using System.Windows;
using System.Windows.Media;
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
        Dispatcher.BeginInvoke(() =>
        {
            var controls = FindVisualChildren<SaleLineControl>(LinesContainer).ToList();
            SaleLineControl? ctrl = target switch
            {
                SalesMainViewModel.FocusTargets.LineProductCode     => controls.FirstOrDefault(),
                SalesMainViewModel.FocusTargets.LineProductCodeLast => controls.LastOrDefault(),
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
