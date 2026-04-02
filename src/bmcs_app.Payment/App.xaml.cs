using System.Windows;
using bmcs_app.Core.Models;
using bmcs_app.Infrastructure.Repositories;
using bmcs_app.Payment.Services;
using bmcs_app.Payment.ViewModels;
using bmcs_app.Payment.Views;

namespace bmcs_app.Payment;

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
        var vm = new PaymentMainViewModel(lookupService, receiptRepo);

        foreach (var pm in paymentMethods)
            vm.PaymentMethods.Add(pm);

        var win = new PaymentMainView { DataContext = vm };
        win.Show();
    }
}
