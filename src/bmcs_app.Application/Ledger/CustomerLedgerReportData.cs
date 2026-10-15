using bmcs_app.Domain.Calculations;

namespace bmcs_app.Application.Ledger;

/// <summary>得意先元帳の印刷用データ（docs/report-spec.md 2-3）。WPF 型を含まない。</summary>
/// <param name="CompanyName">自社名。未登録なら null（印刷は止めない）。</param>
public sealed record CustomerLedgerReportData(
    string? CompanyName,
    string CustomerCode,
    string CustomerName,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    CustomerLedgerResult Ledger);
