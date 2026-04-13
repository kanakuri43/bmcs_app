using bmcs_app.Core.Models;
using bmcs_app.Shared.Views;

namespace bmcs_app.Purchase.Services;

/// <summary>
/// 仕入登録用マスタ検索サービス。
/// 起動時に読み込んだ仕入先・商品リストを保持し、ダイアログ表示とコード補完を提供する。
/// </summary>
public class LookupService
{
    private List<Supplier> _suppliers = new();
    private List<Employee> _employees = new();
    private List<Product>  _products  = new();

    // 伝票検索ダイアログ用（App.xaml.cs から注入）
    private string[]       _slipColumns          = Array.Empty<string>();
    private List<string[]> _slipRows             = new();

    // 発注検索ダイアログ用（App.xaml.cs から注入）
    private string[]       _purchaseOrderColumns = Array.Empty<string>();
    private List<string[]> _purchaseOrderRows    = new();

    /// <summary>
    /// 伝票検索ダイアログ用の非正規化データを設定する。
    /// App.xaml.cs で GetAllFlatAsync() の結果を渡す。
    /// </summary>
    public void SetSlipData(string[] columns, IEnumerable<string[]> rows)
    {
        _slipColumns = columns;
        _slipRows    = rows.ToList();
    }

    public void SetPurchaseOrderData(string[] columns, IEnumerable<string[]> rows)
    {
        _purchaseOrderColumns = columns;
        _purchaseOrderRows    = rows.ToList();
    }

    public void Initialize(
        IEnumerable<Supplier> suppliers,
        IEnumerable<Employee> employees,
        IEnumerable<Product>  products)
    {
        _suppliers = suppliers.ToList();
        _employees = employees.ToList();
        _products  = products.ToList();
    }

    // ── ダイアログ検索 ──────────────────────────────────────
    public Supplier? OpenSupplierSearch(string initialKeyword = "")
    {
        var items = _suppliers.Select(s =>
            new MasterSearchDialog.SearchItem(s.SupplierCode, s.SupplierName, s));
        var dlg = new MasterSearchDialog("仕入先検索", items, initialKeyword)
            { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true
            ? (Supplier)dlg.SelectedSearchItem!.Source
            : null;
    }

    public Employee? OpenEmployeeSearch(string initialKeyword = "")
    {
        var items = _employees.Select(e =>
            new MasterSearchDialog.SearchItem(e.EmployeeCode, e.EmployeeName, e));
        var dlg = new MasterSearchDialog("担当者検索", items, initialKeyword)
            { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true
            ? (Employee)dlg.SelectedSearchItem!.Source
            : null;
    }

    public Product? OpenProductSearch(string initialKeyword = "")
    {
        var items = _products.Select(p =>
            new MasterSearchDialog.SearchItem(p.ProductCode, p.ProductName, p));
        var dlg = new MasterSearchDialog("商品検索", items, initialKeyword)
            { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true
            ? (Product)dlg.SelectedSearchItem!.Source
            : null;
    }

    public string? OpenSlipSearch(string initialKeyword = "")
    {
        var dlg = new SlipSearchDialog("仕入検索", _slipColumns, _slipRows, keyColumnIndex: 1, initialKeyword)
            { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? dlg.SelectedSlipNo : null;
    }

    public string? OpenPurchaseOrderSearch(string initialKeyword = "")
    {
        var dlg = new SlipSearchDialog("発注検索", _purchaseOrderColumns, _purchaseOrderRows, keyColumnIndex: 1, initialKeyword)
            { Owner = System.Windows.Application.Current.MainWindow };
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
