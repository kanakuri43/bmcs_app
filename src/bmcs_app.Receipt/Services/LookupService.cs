using bmcs_app.Core.Models;
using bmcs_app.Shared.Views;

namespace bmcs_app.Receipt.Services;

/// <summary>
/// 入金登録画面で使用するマスタ検索サービス。
/// 起動時に読み込んだ得意先・入金区分リストを保持し、ダイアログ表示とコード補完を提供する。
/// </summary>
public class LookupService
{
    private List<Customer>      _customers      = new();
    private List<PaymentMethod> _paymentMethods = new();

    public void Initialize(
        IEnumerable<Customer>      customers,
        IEnumerable<PaymentMethod> paymentMethods)
    {
        _customers      = customers.ToList();
        _paymentMethods = paymentMethods.ToList();
    }

    // ── ダイアログ検索 ──────────────────────────────────────────
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

    public PaymentMethod? OpenPaymentMethodSearch(string initialKeyword = "")
    {
        var items = _paymentMethods.Select(p =>
            new MasterSearchDialog.SearchItem(p.PaymentMethodCode, p.PaymentMethodName, p));
        var dlg = new MasterSearchDialog("入金区分検索", items, initialKeyword)
            { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true
            ? (PaymentMethod)dlg.SelectedSearchItem!.Source
            : null;
    }

    public string? OpenSlipSearch(IEnumerable<SlipSummary> slips, string initialKeyword = "")
    {
        var items = slips.Select(s =>
            new MasterSearchDialog.SearchItem(
                s.SlipNo,
                $"{s.SlipDate:yyyy/MM/dd}  {s.CustomerName}",
                s.SlipNo));
        var dlg = new MasterSearchDialog("伝票検索", items, initialKeyword)
            { Owner = System.Windows.Application.Current.MainWindow };
        return dlg.ShowDialog() == true
            ? (string)dlg.SelectedSearchItem!.Source
            : null;
    }

    // ── コード直接補完 ───────────────────────────────────────────
    public Customer? FindCustomerByCode(string code)
        => _customers.Find(c => c.CustomerCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));

    public PaymentMethod? FindPaymentMethodByCode(string code)
        => _paymentMethods.Find(p => p.PaymentMethodCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase));
}
