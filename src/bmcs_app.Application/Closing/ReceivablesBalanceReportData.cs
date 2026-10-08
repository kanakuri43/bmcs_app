namespace bmcs_app.Application.Closing;

/// <summary>
/// 売掛金残高一覧表の1行（確定済みの <c>monthly_closings</c> 1件）。
/// <see cref="IsBillingChild"/> が真の行（請求集約元）は、前月残高・入金額・消費税・当月残高が
/// 0で保存されており（<c>docs/design_document.md</c> 29-1節）、売上額だけが意味を持つ。
/// </summary>
public sealed record ReceivablesBalanceReportRow(
    string CustomerCode,
    string CustomerName,
    bool IsBillingChild,
    decimal PreviousBalance,
    decimal ReceiptAmount,
    decimal SalesAmount,
    decimal TaxAmount,
    decimal ClosingBalance);

/// <summary>
/// 売掛金残高一覧表の印刷データ（WPF型を含まない）。合計は請求集約元の行を除いて集計する
/// （請求集約元の売上は請求集約先の行に含まれており、足すと二重に数えるため）。
/// </summary>
public sealed record ReceivablesBalanceReportData(
    int Year,
    int Month,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string? CompanyName,
    IReadOnlyList<ReceivablesBalanceReportRow> Rows)
{
    private IEnumerable<ReceivablesBalanceReportRow> TotalTargets => Rows.Where(r => !r.IsBillingChild);

    public decimal TotalPreviousBalance => TotalTargets.Sum(r => r.PreviousBalance);

    public decimal TotalReceiptAmount => TotalTargets.Sum(r => r.ReceiptAmount);

    public decimal TotalSalesAmount => TotalTargets.Sum(r => r.SalesAmount);

    public decimal TotalTaxAmount => TotalTargets.Sum(r => r.TaxAmount);

    public decimal TotalClosingBalance => TotalTargets.Sum(r => r.ClosingBalance);

    /// <summary>請求集約元の行を含むか（帳票の注記を出すかどうかの判定に使う）。</summary>
    public bool HasBillingChild => Rows.Any(r => r.IsBillingChild);
}
