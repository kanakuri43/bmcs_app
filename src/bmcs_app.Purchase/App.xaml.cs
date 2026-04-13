using System.Windows;
using bmcs_app.Infrastructure.Repositories;
using bmcs_app.Purchase.Services;
using bmcs_app.Purchase.ViewModels;
using bmcs_app.Purchase.Views;

namespace bmcs_app.Purchase;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var supplierRepo      = new SupplierRepository();
        var employeeRepo      = new EmployeeRepository();
        var productRepo       = new ProductRepository();

        var suppliers = Task.Run(() => supplierRepo.GetAllAsync()).Result;
        var employees = Task.Run(() => employeeRepo.GetAllAsync()).Result;
        var products  = Task.Run(() => productRepo.GetAllAsync()).Result;

        var purchaseRepo     = new PurchaseRepository();
        var purchaseFlat     = Task.Run(() => purchaseRepo.GetAllFlatAsync()).Result;
        var purchaseOrderRepo = new PurchaseOrderRepository();
        var purchaseOrderFlat = Task.Run(() => purchaseOrderRepo.GetAllFlatAsync()).Result;

        var lookupService = new LookupService();
        lookupService.Initialize(suppliers, employees, products);
        lookupService.SetSlipData(
            new[] { "日付", "仕入番号", "仕入先コード", "仕入先名", "行", "商品コード", "商品名", "数量", "単価", "金額" },
            purchaseFlat);
        lookupService.SetPurchaseOrderData(
            new[] { "日付", "発注番号", "仕入先コード", "仕入先名", "行", "商品コード", "商品名", "数量", "単価", "金額" },
            purchaseOrderFlat);

        var taxRatePeriodRepo = new TaxRatePeriodRepository();
        var taxRatePeriods    = Task.Run(() => taxRatePeriodRepo.GetAllAsync()).Result;

        var vm = new PurchaseMainViewModel(lookupService, purchaseRepo, purchaseOrderRepo);
        vm.SetTaxRatePeriods(taxRatePeriods);

        _ = InitTaxTypesAsync(vm);

        var win = new PurchaseMainView { DataContext = vm };
        win.Show();

        var initialNo = e.Args
            .Select(a => a.StartsWith("--purchase-no=") ? a["--purchase-no=".Length..] : null)
            .FirstOrDefault(v => v is not null);
        if (initialNo is not null)
            _ = vm.LoadInitialSlipAsync(initialNo);
    }

    private static async Task InitTaxTypesAsync(PurchaseMainViewModel vm)
    {
        try
        {
            var repo  = new TaxTypeRepository();
            var types = await repo.GetAllAsync();
            foreach (var t in types)
                vm.TaxTypes.Add(t);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"税種別の読み込みエラー: {ex.Message}";
        }
    }
}
