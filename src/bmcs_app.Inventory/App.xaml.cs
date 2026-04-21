using System.Windows;
using bmcs_app.Infrastructure.Repositories;
using bmcs_app.Inventory.Services;
using bmcs_app.Inventory.ViewModels;
using bmcs_app.Inventory.Views;

namespace bmcs_app.Inventory;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var showCount = e.Args.Any(a => a == "--view=count");

        if (showCount)
        {
            var productRepo = new ProductRepository();
            var products    = Task.Run(() => productRepo.GetAllAsync()).Result;
            var lookup      = new LookupService();
            lookup.Initialize(products);

            var countRepo = new InventoryCountRepository();
            var vm        = new InventoryCountViewModel(countRepo, lookup);
            var win       = new InventoryCountView { DataContext = vm };
            win.Show();
        }
        else
        {
            var currentRepo = new InventoryCurrentRepository();
            var vm          = new InventoryInquiryViewModel(currentRepo);
            var win         = new InventoryInquiryView { DataContext = vm };
            win.Show();
        }
    }
}
