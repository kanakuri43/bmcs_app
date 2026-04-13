using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using bmcs_app.Purchase.ViewModels;

namespace bmcs_app.Purchase.Views;

public partial class PurchaseMainView : MetroWindow
{
    public PurchaseMainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PurchaseMainViewModel oldVm)
            oldVm.FocusField -= OnFocusField;
        if (e.NewValue is PurchaseMainViewModel newVm)
            newVm.FocusField += OnFocusField;
    }

    private void OnFocusField(string target)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var controls = FindVisualChildren<PurchaseLineControl>(LinesContainer).ToList();
            PurchaseLineControl? ctrl = target switch
            {
                PurchaseMainViewModel.FocusTargets.LineProductCode     => controls.FirstOrDefault(),
                PurchaseMainViewModel.FocusTargets.LineProductCodeLast => controls.LastOrDefault(),
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
