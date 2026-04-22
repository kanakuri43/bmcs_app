using System.Windows;
using bmcs_app.Infrastructure.Repositories;
using bmcs_app.Order.Services;
using bmcs_app.Order.ViewModels;
using bmcs_app.Order.Views;

namespace bmcs_app.Order;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // マスタデータを同期ロード
        var customerRepo = new CustomerRepository();
        var employeeRepo = new EmployeeRepository();
        var productRepo  = new ProductRepository();

        var customers = Task.Run(() => customerRepo.GetAllAsync()).Result;
        var employees = Task.Run(() => employeeRepo.GetAllAsync()).Result;
        var products  = Task.Run(() => productRepo.GetAllAsync()).Result;

        // 受注伝票フラットデータ（伝票検索ダイアログ用）
        var orderRepo = new OrderRepository();
        var orderFlat = Task.Run(() => orderRepo.GetAllFlatAsync()).Result;

        var lookupService = new LookupService();
        lookupService.Initialize(customers, employees, products);
        lookupService.SetSlipData(
            new[] { "日付", "受注番号", "得意先コード", "得意先名", "行", "商品コード", "商品名", "数量", "単価", "金額" },
            orderFlat);

        // 税率期間
        var taxRatePeriodRepo = new TaxRatePeriodRepository();
        var taxRatePeriods    = Task.Run(() => taxRatePeriodRepo.GetAllAsync()).Result;

        var vm = new OrderMainViewModel(lookupService, orderRepo);
        vm.SetTaxRatePeriods(taxRatePeriods);

        var win = new OrderMainView { DataContext = vm };
        win.Show();

        // コマンドライン引数で受注No.が指定されている場合はそれを表示
        var initialOrderNo = e.Args
            .Select(a => a.StartsWith("--order-no=") ? a["--order-no=".Length..] : null)
            .FirstOrDefault(v => v is not null);
        if (initialOrderNo is not null)
            _ = vm.LoadInitialSlipAsync(initialOrderNo);
    }

}
