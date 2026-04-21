using bmcs_app.Core.Models;
using bmcs_app.Shared.Views;
using System.Windows;

namespace bmcs_app.Inventory.Services;

public class LookupService
{
    private List<Product> _products = new();

    public void Initialize(IEnumerable<Product> products)
    {
        _products = products.ToList();
    }

    public Product? OpenProductSearch(string initialKeyword = "")
    {
        var items = _products.Select(p =>
            new MasterSearchDialog.SearchItem(p.ProductCode, p.ProductName, p));
        var dlg = new MasterSearchDialog("商品検索", items, initialKeyword)
            { Owner = Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? (Product)dlg.SelectedSearchItem!.Source : null;
    }

    public Product? FindProductByCode(string code)
        => _products.Find(p => p.ProductCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
}
