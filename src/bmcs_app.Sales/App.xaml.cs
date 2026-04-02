using System.Windows;
using bmcs_app.Core.Models;
using bmcs_app.Infrastructure.Repositories;
using bmcs_app.Sales.Services;
using bmcs_app.Sales.ViewModels;
using bmcs_app.Sales.Views;

namespace bmcs_app.Sales;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // マスタデータを同期ロードして LookupService に渡す
        var customerRepo = new CustomerRepository();
        var employeeRepo = new EmployeeRepository();
        var productRepo  = new ProductRepository();

        var customers = Task.Run(() => customerRepo.GetAllAsync()).Result;
        var employees = Task.Run(() => employeeRepo.GetAllAsync()).Result;
        var products  = Task.Run(() => productRepo.GetAllAsync()).Result;

        // 税種別は Infrastructure に専用リポジトリが不要なため Customer 経由で取得済みデータを使用
        // TaxTypes は ViewModel 初期化後に設定する
        var lookupService = new LookupService();
        lookupService.Initialize(customers, employees, products);

        var taxRatePeriodRepo = new TaxRatePeriodRepository();
        var taxRatePeriods    = Task.Run(() => taxRatePeriodRepo.GetAllAsync()).Result;

        var companyInfo = Task.Run(() => new CompanyInfoRepository().GetAsync()).Result;

        var saleRepo = new SaleRepository();
        var vm = new SalesMainViewModel(lookupService, saleRepo);
        vm.SetTaxRatePeriods(taxRatePeriods);
        vm.SetCompanyInfo(companyInfo);

        // 税種別を VM の TaxTypes コレクションにセット（ダイアログとの照合用）
        _ = InitTaxTypesAsync(vm);

        var win = new SalesMainView { DataContext = vm };
        win.Show();

        var initialSlipNo = e.Args
            .Select(a => a.StartsWith("--slip-no=") ? a["--slip-no=".Length..] : null)
            .FirstOrDefault(v => v is not null);
        if (initialSlipNo is not null)
            _ = vm.LoadInitialSlipAsync(initialSlipNo);
    }

    private static async Task InitTaxTypesAsync(SalesMainViewModel vm)
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
