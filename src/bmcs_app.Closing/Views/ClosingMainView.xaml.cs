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
            static bool Confirm(string message, string title)
            {
                var result = System.Windows.MessageBox.Show(
                    message, title,
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.Cancel);
                return result == System.Windows.MessageBoxResult.OK;
            }

            vm.InvoiceTab.ConfirmCancel = msg => Confirm(msg, "締め解除の確認");
            vm.ArTab.ConfirmCancel      = msg => Confirm(msg, "集計取り消しの確認");
        }
    }
}
