using System.Windows;
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

        var supplierRepo      = new SupplierRepository();
        var paymentMethodRepo = new PaymentMethodRepository();

        var suppliers      = Task.Run(() => supplierRepo.GetAllAsync()).Result;
        var paymentMethods = Task.Run(() => paymentMethodRepo.GetAllAsync()).Result;

        var paymentRepo = new PaymentRepository();
        var paymentFlat = Task.Run(() => paymentRepo.GetAllFlatAsync()).Result;

        var lookupService = new LookupService();
        lookupService.Initialize(suppliers, paymentMethods);
        lookupService.SetSlipData(
            new[] { "日付", "支払番号", "仕入先コード", "仕入先名", "行", "支払区分", "金額", "手形期日", "行摘要" },
            paymentFlat);

        var vm = new PaymentMainViewModel(lookupService, paymentRepo);

        foreach (var pm in paymentMethods)
            vm.PaymentMethods.Add(pm);

        var win = new PaymentMainView { DataContext = vm };
        win.Show();

        var initialNo = e.Args
            .Select(a => a.StartsWith("--payment-no=") ? a["--payment-no=".Length..] : null)
            .FirstOrDefault(v => v is not null);
        if (initialNo is not null)
            _ = vm.LoadInitialSlipAsync(initialNo);
    }
}
