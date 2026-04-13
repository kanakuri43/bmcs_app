using System.Windows;
using bmcs_app.Infrastructure.Repositories;
using bmcs_app.PurchaseSearch.ViewModels;
using bmcs_app.PurchaseSearch.Views;

namespace bmcs_app.PurchaseSearch;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var searchRepo = new PurchaseSearchRepository();
        var vm         = new PurchaseSearchMainViewModel(searchRepo);
        var win        = new PurchaseSearchMainView { DataContext = vm };
        win.Show();
    }
}
