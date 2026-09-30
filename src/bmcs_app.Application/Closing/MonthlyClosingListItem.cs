using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Closing;

/// <summary>月次締め処理画面の一覧1行（確定済みの <c>monthly_closings</c>）。</summary>
public sealed record MonthlyClosingListItem(
    string CustomerCode,
    string CustomerName,
    TaxUnit TaxUnit,
    decimal PreviousBalance,
    decimal ReceiptAmount,
    decimal SalesAmount,
    decimal TaxAmount,
    decimal ClosingBalance,
    DateTime ConfirmedAt);
