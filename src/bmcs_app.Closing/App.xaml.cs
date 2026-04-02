using System.Windows;
using bmcs_app.Closing.ViewModels;
using bmcs_app.Closing.Views;
using bmcs_app.Infrastructure.Repositories;

namespace bmcs_app.Closing;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var customerRepo = new CustomerRepository();
        var customers    = Task.Run(() => customerRepo.GetAllAsync()).Result;

        var vm  = new ClosingMainViewModel(customers);
        var win = new ClosingMainView { DataContext = vm };
        win.Show();
    }
}
