using System.Windows;
using bmcs_app.Infrastructure.Repositories;
using bmcs_app.Search.ViewModels;
using bmcs_app.Search.Views;

namespace bmcs_app.Search;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var searchRepo = new SearchRepository();
        var vm         = new SearchMainViewModel(searchRepo);
        var win        = new SearchMainView { DataContext = vm };
        win.Show();
    }
}
