using System.Windows;
using bmcs_app.Infrastructure.Repositories;
using bmcs_app.PurchaseOrder.Services;
using bmcs_app.PurchaseOrder.ViewModels;
using bmcs_app.PurchaseOrder.Views;

namespace bmcs_app.PurchaseOrder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // マスタデータを同期ロード
        var supplierRepo = new SupplierRepository();
        var employeeRepo = new EmployeeRepository();
        var productRepo  = new ProductRepository();

        var suppliers = Task.Run(() => supplierRepo.GetAllAsync()).Result;
        var employees = Task.Run(() => employeeRepo.GetAllAsync()).Result;
        var products  = Task.Run(() => productRepo.GetAllAsync()).Result;

        // 発注伝票フラットデータ（伝票検索ダイアログ用）
        var purchaseOrderRepo = new PurchaseOrderRepository();
        var purchaseOrderFlat = Task.Run(() => purchaseOrderRepo.GetAllFlatAsync()).Result;

        var lookupService = new LookupService();
        lookupService.Initialize(suppliers, employees, products);
        lookupService.SetSlipData(
            new[] { "日付", "発注番号", "仕入先コード", "仕入先名", "行", "商品コード", "商品名", "数量", "単価", "金額" },
            purchaseOrderFlat);

        // 税率期間
        var taxRatePeriodRepo = new TaxRatePeriodRepository();
        var taxRatePeriods    = Task.Run(() => taxRatePeriodRepo.GetAllAsync()).Result;

        var vm = new PurchaseOrderMainViewModel(lookupService, purchaseOrderRepo);
        vm.SetTaxRatePeriods(taxRatePeriods);

        // 税種別を非同期ロード（ComboBox 用）
        _ = InitTaxTypesAsync(vm);

        var win = new PurchaseOrderMainView { DataContext = vm };
        win.Show();

        var initialNo = e.Args
            .Select(a => a.StartsWith("--purchase-order-no=") ? a["--purchase-order-no=".Length..] : null)
            .FirstOrDefault(v => v is not null);
        if (initialNo is not null)
            _ = vm.LoadInitialSlipAsync(initialNo);
    }

    private static async Task InitTaxTypesAsync(PurchaseOrderMainViewModel vm)
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
