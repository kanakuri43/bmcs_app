using System.Windows;
using bmcs_app.Infrastructure.Repositories;
using bmcs_app.Master.ViewModels;
using bmcs_app.Master.Views;
using bmcs_app.Core.Interfaces;

namespace bmcs_app.Master;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var master = "employee";
        foreach (var arg in e.Args)
        {
            if (arg.StartsWith("--master=", StringComparison.OrdinalIgnoreCase))
            {
                master = arg["--master=".Length..].ToLowerInvariant();
                break;
            }
        }

        Window win = master switch
        {
            "customer" => CreateCustomerWindow(),
            "taxrate"  => CreateTaxRatePeriodWindow(),
            _          => CreateEmployeeWindow(),
        };

        win.Show();
    }

    private static Window CreateEmployeeWindow()
    {
        var repo = new EmployeeRepository();
        var vm   = new EmployeeMaintViewModel(repo);
        return new EmployeeMaintView { DataContext = vm };
    }

    private static Window CreateCustomerWindow()
    {
        var repo = new CustomerRepository();
        var vm   = new CustomerMaintViewModel(repo);
        return new CustomerMaintView { DataContext = vm };
    }

    private static Window CreateTaxRatePeriodWindow()
    {
        var repo = new TaxRatePeriodRepository();
        var vm   = new TaxRatePeriodMaintViewModel(repo);
        return new TaxRatePeriodMaintView { DataContext = vm };
    }
}
