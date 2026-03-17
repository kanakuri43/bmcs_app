using System.Windows;
using bmcs_app.Infrastructure.Repositories;
using bmcs_app.Master.ViewModels;
using bmcs_app.Master.Views;

namespace bmcs_app.Master;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var repo = new EmployeeRepository();
        var vm   = new EmployeeMaintViewModel(repo);
        var win  = new EmployeeMaintView { DataContext = vm };
        win.Show();
    }
}
