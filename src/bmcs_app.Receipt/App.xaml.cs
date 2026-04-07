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

        var receiptRepo = new ReceiptRepository();
        var receiptFlat = Task.Run(() => receiptRepo.GetAllFlatAsync()).Result;

        var lookupService = new LookupService();
        lookupService.Initialize(customers, paymentMethods);
        lookupService.SetSlipData(
            new[] { "日付", "伝票番号", "得意先コード", "得意先名", "行", "入金区分", "金額", "手形期日", "行摘要" },
            receiptFlat);

        var vm = new ReceiptMainViewModel(lookupService, receiptRepo);

        foreach (var pm in paymentMethods)
            vm.PaymentMethods.Add(pm);

        var win = new ReceiptMainView { DataContext = vm };
        win.Show();

        var initialSlipNo = e.Args
            .Select(a => a.StartsWith("--slip-no=") ? a["--slip-no=".Length..] : null)
            .FirstOrDefault(v => v is not null);
        if (initialSlipNo is not null)
            _ = vm.LoadInitialSlipAsync(initialSlipNo);
    }
}
