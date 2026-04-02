using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.Controls;
using bmcs_app.Search.ViewModels;

namespace bmcs_app.Search.Views;

public partial class SearchMainView : MetroWindow
{
    public SearchMainView() => InitializeComponent();

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // VisualTree を上に辿って DataGridRow を探す（ヘッダや空白行クリックを除外）
        var dep = e.OriginalSource as DependencyObject;
        while (dep is not null and not DataGridRow)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is not DataGridRow) return;

        if (DataContext is SearchMainViewModel vm && vm.OpenSlipCommand.CanExecute())
            vm.OpenSlipCommand.Execute();
    }
}
