using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using bmcs_app.Closing.ViewModels;

namespace bmcs_app.Closing.Views;

public partial class ClosingMainView : MetroWindow
{
    public ClosingMainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is ClosingMainViewModel vm)
        {
            vm.InvoiceTab.ConfirmCancel = message =>
            {
                var result = System.Windows.MessageBox.Show(
                    message,
                    "締め解除の確認",
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.Cancel);
                return result == System.Windows.MessageBoxResult.OK;
            };
        }
    }
}
