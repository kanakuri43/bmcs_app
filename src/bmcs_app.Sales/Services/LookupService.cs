using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using bmcs_app.Shared.Views;

namespace bmcs_app.Sales.Services;

/// <summary>
/// マスタ検索ダイアログを使った ILookupService 実装。
/// 起動時に読み込んだマスタリストを保持し、ダイアログ表示とコード補完を提供する。
/// </summary>
public class LookupService : ILookupService
{
    private List<Customer> _customers = new();
    private List<Employee> _employees = new();
    private List<Product>  _products  = new();

    // 伝票検索ダイアログ用（App.xaml.cs から注入）
    private string[]       _slipColumns = Array.Empty<string>();
    private List<string[]> _slipRows    = new();

    /// <summary>
    /// 伝票検索ダイアログ用の非正規化データを設定する。
    /// App.xaml.cs で GetAllFlatAsync() の結果を渡す。
    /// </summary>
    public void SetSlipData(string[] columns, IEnumerable<string[]> rows)
    {
        _slipColumns = columns;
        _slipRows    = rows.ToList();
    }

    public void Initialize(
        IEnumerable<Customer> customers,
        IEnumerable<Employee> employees,
        IEnumerable<Product>  products)
    {
        _customers = customers.ToList();
        _employees = employees.ToList();
        _products  = products.ToList();
    }

    // ── ダイアログ検索 ──────────────────────────────────────
    public Customer? OpenCustomerSearch(string initialKeyword = "")
    {
        var items = _customers.Select(c =>
            new MasterSearchDialog.SearchItem(c.CustomerCode, c.CustomerName, c));
        var dlg = new MasterSearchDialog("得意先検索", items, initialKeyword)
            { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true
            ? (Customer)dlg.SelectedSearchItem!.Source
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

    public string? OpenSlipSearch(IEnumerable<SlipSummary> slips, string initialKeyword = "")
    {
        var dlg = new SlipSearchDialog("伝票検索", _slipColumns, _slipRows, keyColumnIndex: 1, initialKeyword)
            { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? dlg.SelectedSlipNo : null;
    }

    // ── コード直接補完 ─────────────────────────────────────
    public Customer? FindCustomerByCode(string code)
        => _customers.Find(c => c.CustomerCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    public Employee? FindEmployeeByCode(string code)
        => _employees.Find(e => e.EmployeeCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    public Employee? FindEmployeeById(int id)
        => _employees.Find(e => e.EmployeeId == id);

    public Product? FindProductByCode(string code)
        => _products.Find(p => p.ProductCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
}
