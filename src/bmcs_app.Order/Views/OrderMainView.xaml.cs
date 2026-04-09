using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using bmcs_app.Order.ViewModels;

namespace bmcs_app.Order.Views;

public partial class OrderMainView : MetroWindow
{
    public OrderMainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is OrderMainViewModel oldVm)
            oldVm.FocusField -= OnFocusField;
        if (e.NewValue is OrderMainViewModel newVm)
            newVm.FocusField += OnFocusField;
    }

    private void OnFocusField(string target)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var controls = FindVisualChildren<OrderLineControl>(LinesContainer).ToList();
            OrderLineControl? ctrl = target switch
            {
                OrderMainViewModel.FocusTargets.LineProductCode     => controls.FirstOrDefault(),
                OrderMainViewModel.FocusTargets.LineProductCodeLast => controls.LastOrDefault(),
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
