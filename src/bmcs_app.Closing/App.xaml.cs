using System.Windows;
using bmcs_app.Closing.ViewModels;
using bmcs_app.Closing.Views;
using bmcs_app.Infrastructure;
using bmcs_app.Infrastructure.Repositories;

namespace bmcs_app.Closing;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var customerRepo = new CustomerRepository();
        var companyRepo  = new CompanyInfoRepository();
        var closingRepo  = new ClosingRepository();

        var customers   = Task.Run(() => customerRepo.GetAllAsync()).Result;
        var companyInfo = Task.Run(() => companyRepo.GetAsync()).Result;

        var vm  = new ClosingMainViewModel(customers, closingRepo);
        vm.InvoiceTab.SetCompanyInfo(companyInfo);
        vm.ArTab.SetCompanyInfo(companyInfo);

        var win = new ClosingMainView { DataContext = vm };
        win.Show();
    }
}
