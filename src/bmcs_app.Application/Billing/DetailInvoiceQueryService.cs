using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 明細請求書の検索ユースケース（伝票検索モーダル用）。<see cref="DetailInvoiceService"/> は
/// 採番・発行・取消のコマンドサービスのため、読み取り専用の検索はこちらへ分離する
/// （<see cref="Sales.SalesQueryService"/> / <see cref="Order.OrderQueryService"/> と対称）。
/// </summary>
public class DetailInvoiceQueryService(BmcsDbContext dbContext)
{
    private const int MaxSearchResults = 200;

    /// <summary>
    /// 伝票検索モーダル用。明細請求書番号・得意先コード・得意先名・宛名のいずれかにキーワードを
    /// 含む明細請求書を新しい順で返す。取消済みも含める（<see cref="DetailInvoiceService.GetByNumberAsync"/>
    /// と同じく <c>IsDeleted</c> のみで除外する）。
    /// </summary>
    public async Task<List<DetailInvoiceHit>> SearchAsync(
        string? keyword, CancellationToken cancellationToken = default)
    {
        var query = dbContext.DetailInvoices.AsNoTracking().Where(d => !d.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(d =>
                d.DetailInvoiceNumber.Contains(keyword)
                || d.CustomerCode.Contains(keyword)
                || d.CustomerName.Contains(keyword)
                || d.AddresseeName.Contains(keyword));
        }

        var rows = await query
            .OrderByDescending(d => d.IssueDate)
            .ThenByDescending(d => d.DetailInvoiceNumber)
            .Take(MaxSearchResults)
            .ToListAsync(cancellationToken);

        return rows
            .Select(d => new DetailInvoiceHit(
                d.DetailInvoiceNumber,
                d.IssueDate,
                d.CustomerCode,
                d.CustomerName,
                d.AddresseeName,
                d.TotalAmount,
                d.InvoiceStatus))
            .ToList();
    }
}
