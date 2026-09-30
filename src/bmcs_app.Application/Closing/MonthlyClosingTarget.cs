using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Closing;

/// <summary>
/// 月次締め処理（TODO.md 9-1）の得意先1件分の結果。
/// </summary>
/// <param name="SkipReason">非null＝この得意先は月次締めの行を作らなかった。理由は画面にそのまま表示する。</param>
public sealed record MonthlyClosingTarget(
    string CustomerCode,
    string CustomerName,
    TaxUnit TaxUnit,
    decimal PreviousBalance,
    decimal SalesAmount,
    decimal ReceiptAmount,
    decimal TaxAmount,
    decimal ClosingBalance,
    string? SkipReason);
