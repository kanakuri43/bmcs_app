using System.Windows;
using bmcs_app.Views;

namespace bmcs_app;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var vm  = new ViewModels.MainWindowViewModel();
        var win = new MainWindow { DataContext = vm };
        win.Show();
    }
}
