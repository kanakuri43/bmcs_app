using System.Windows;
using System.Windows.Media;
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
            var controls = FindVisualChildren<PaymentLineControl>(LinesContainer).ToList();
            controls.LastOrDefault()?.FocusPaymentMethod();
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
