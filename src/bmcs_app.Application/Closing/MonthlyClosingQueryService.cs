using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Closing;

/// <summary>月次締め処理画面の一覧用の読み取り専用照会（TODO.md 9-1）。</summary>
public class MonthlyClosingQueryService(BmcsDbContext dbContext)
{
    /// <summary>指定した年月の確定済み（解除済みを除く）月次締めを、得意先コード順に返す。</summary>
    public Task<List<MonthlyClosingListItem>> GetByMonthAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        var closingDate = MonthlyClosingService.MonthEnd(year, month);

        return dbContext.MonthlyClosings
            .AsNoTracking()
            .Where(m => m.ClosingDate == closingDate && !m.IsDeleted && m.ClosingStatus == ClosingStatus.Confirmed)
            .OrderBy(m => m.CustomerCode)
            .Select(m => new MonthlyClosingListItem(
                m.CustomerCode,
                m.CustomerName,
                m.TaxUnit,
                m.PreviousBalance,
                m.ReceiptAmount,
                m.SalesAmount,
                m.TaxAmount,
                m.ClosingBalance,
                m.ConfirmedAt))
            .ToListAsync(cancellationToken);
    }
}
