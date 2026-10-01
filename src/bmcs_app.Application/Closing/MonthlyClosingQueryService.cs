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

    /// <summary>
    /// 売掛金残高一覧表の印刷データ。抽出条件は <see cref="GetByMonthAsync"/> と同じ。
    /// 請求集約元かどうかは <c>monthly_closings</c> に持たないため、得意先マスタの請求得意先コードで判定する。
    /// 自社名は帳票の見出し用で、自社情報が未登録でも印刷は止めない。
    /// </summary>
    public async Task<ReceivablesBalanceReportData> GetReceivablesBalanceReportAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        var closingDate = MonthlyClosingService.MonthEnd(year, month);

        var rows = await (
            from m in dbContext.MonthlyClosings.AsNoTracking()
            where m.ClosingDate == closingDate && !m.IsDeleted && m.ClosingStatus == ClosingStatus.Confirmed
            join c in dbContext.Customers.AsNoTracking() on m.CustomerCode equals c.CustomerCode into customers
            from c in customers.DefaultIfEmpty()
            orderby m.CustomerCode
            select new ReceivablesBalanceReportRow(
                m.CustomerCode,
                m.CustomerName,
                c != null && c.BillingCustomerCode != c.CustomerCode,
                m.PreviousBalance,
                m.ReceiptAmount,
                m.SalesAmount,
                m.TaxAmount,
                m.ClosingBalance))
            .ToListAsync(cancellationToken);

        var companyName = await dbContext.CompanyInfos
            .AsNoTracking()
            .Select(c => c.CompanyName)
            .FirstOrDefaultAsync(cancellationToken);

        return new ReceivablesBalanceReportData(
            year, month, new DateOnly(year, month, 1), closingDate, companyName, rows);
    }
}
