using System.Windows;
using bmcs_app.Core.Models;
using bmcs_app.Infrastructure.Repositories;
using bmcs_app.Receipt.Services;
using bmcs_app.Receipt.ViewModels;
using bmcs_app.Receipt.Views;

namespace bmcs_app.Receipt;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var customerRepo      = new CustomerRepository();
        var paymentMethodRepo = new PaymentMethodRepository();

        var customers      = Task.Run(() => customerRepo.GetAllAsync()).Result;
        var paymentMethods = Task.Run(() => paymentMethodRepo.GetAllAsync()).Result;

        var lookupService = new LookupService();
        lookupService.Initialize(customers, paymentMethods);

        var receiptRepo = new ReceiptRepository();
        var vm = new ReceiptMainViewModel(lookupService, receiptRepo);

        foreach (var pm in paymentMethods)
            vm.PaymentMethods.Add(pm);

        var win = new ReceiptMainView { DataContext = vm };
        win.Show();
    }
}
