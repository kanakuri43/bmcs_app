using bmcs_app.Core.Models;
using bmcs_app.Shared.Views;
using System.Windows;

namespace bmcs_app.PurchaseOrder.Services;

/// <summary>
/// 発注登録用マスタ検索サービス。
/// 起動時に読み込んだ仕入先・商品リストを保持し、ダイアログ表示とコード補完を提供する。
/// </summary>
public class LookupService
{
    private List<Supplier> _suppliers = new();
    private List<Product>  _products  = new();
    private List<Employee> _employees = new();

    private string[]       _slipColumns = Array.Empty<string>();
    private List<string[]> _slipRows    = new();

    public void Initialize(IEnumerable<Supplier> suppliers, IEnumerable<Employee> employees, IEnumerable<Product> products)
    {
        _suppliers = suppliers.ToList();
        _employees = employees.ToList();
        _products  = products.ToList();
    }

    public void SetSlipData(string[] columns, IEnumerable<string[]> rows)
    {
        _slipColumns = columns;
        _slipRows    = rows.ToList();
    }

    // ── ダイアログ検索 ──────────────────────────────────────
    public Supplier? OpenSupplierSearch(string initialKeyword = "")
    {
        var items = _suppliers.Select(s =>
            new MasterSearchDialog.SearchItem(s.SupplierCode, s.SupplierName, s));
        var dlg = new MasterSearchDialog("仕入先検索", items, initialKeyword)
            { Owner = Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? (Supplier)dlg.SelectedSearchItem!.Source : null;
    }

    public Product? OpenProductSearch(string initialKeyword = "")
    {
        var items = _products.Select(p =>
            new MasterSearchDialog.SearchItem(p.ProductCode, p.ProductName, p));
        var dlg = new MasterSearchDialog("商品検索", items, initialKeyword)
            { Owner = Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? (Product)dlg.SelectedSearchItem!.Source : null;
    }

    public Employee? OpenEmployeeSearch(string initialKeyword = "")
    {
        var items = _employees.Select(e =>
            new MasterSearchDialog.SearchItem(e.EmployeeCode, e.EmployeeName, e));
        var dlg = new MasterSearchDialog("担当者検索", items, initialKeyword)
            { Owner = Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? (Employee)dlg.SelectedSearchItem!.Source : null;
    }

    public string? OpenSlipSearch(string initialKeyword = "")
    {
        var dlg = new SlipSearchDialog("発注検索", _slipColumns, _slipRows, keyColumnIndex: 1, initialKeyword)
            { Owner = Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? dlg.SelectedSlipNo : null;
    }

    // ── コード直接補完 ─────────────────────────────────────
    public Supplier? FindSupplierByCode(string code)
        => _suppliers.Find(s => s.SupplierCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    public Supplier? FindSupplierById(int id)
        => _suppliers.Find(s => s.SupplierId == id);

    public Employee? FindEmployeeByCode(string code)
        => _employees.Find(e => e.EmployeeCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    public Employee? FindEmployeeById(int id)
        => _employees.Find(e => e.EmployeeId == id);

    public Product? FindProductByCode(string code)
        => _products.Find(p => p.ProductCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
}
