using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.Controls;
using bmcs_app.PurchaseSearch.ViewModels;

namespace bmcs_app.PurchaseSearch.Views;

public partial class PurchaseSearchMainView : MetroWindow
{
    public PurchaseSearchMainView() => InitializeComponent();

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null and not DataGridRow)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is not DataGridRow) return;

        if (DataContext is PurchaseSearchMainViewModel vm && vm.OpenSlipCommand.CanExecute())
            vm.OpenSlipCommand.Execute();
    }
}
