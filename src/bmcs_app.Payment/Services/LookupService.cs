using bmcs_app.Core.Models;
using bmcs_app.Shared.Views;

namespace bmcs_app.Payment.Services;

/// <summary>
/// 支払登録画面で使用するマスタ検索サービス。
/// 起動時に読み込んだ仕入先・支払区分リストを保持し、ダイアログ表示とコード補完を提供する。
/// </summary>
public class LookupService
{
    private List<Supplier>      _suppliers      = new();
    private List<PaymentMethod> _paymentMethods = new();

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
        IEnumerable<Supplier>      suppliers,
        IEnumerable<PaymentMethod> paymentMethods)
    {
        _suppliers      = suppliers.ToList();
        _paymentMethods = paymentMethods.ToList();
    }

    // ── ダイアログ検索 ──────────────────────────────────────────
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

    public PaymentMethod? OpenPaymentMethodSearch(string initialKeyword = "")
    {
        var items = _paymentMethods.Select(p =>
            new MasterSearchDialog.SearchItem(p.PaymentMethodCode, p.PaymentMethodName, p));
        var dlg = new MasterSearchDialog("支払区分検索", items, initialKeyword)
            { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true
            ? (PaymentMethod)dlg.SelectedSearchItem!.Source
            : null;
    }

    public string? OpenSlipSearch(string initialKeyword = "")
    {
        var dlg = new SlipSearchDialog("支払検索", _slipColumns, _slipRows, keyColumnIndex: 1, initialKeyword)
            { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? dlg.SelectedSlipNo : null;
    }

    // ── コード直接補完 ───────────────────────────────────────────
    public Supplier? FindSupplierByCode(string code)
        => _suppliers.Find(s => s.SupplierCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    public PaymentMethod? FindPaymentMethodByCode(string code)
        => _paymentMethods.Find(p => p.PaymentMethodCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
}
