using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Receipt;

/// <summary>
/// 締め入金の検索ユースケース（伝票検索モーダル用）。<see cref="ReceiptEntryService"/> は
/// 登録・照会のコマンドサービスのため、読み取り専用の検索はこちらへ分離する
/// （<see cref="Sales.SalesQueryService"/> / <see cref="Billing.DetailInvoiceQueryService"/> と対称）。
/// </summary>
public class ReceiptQueryService(BmcsDbContext dbContext)
{
    private const int MaxSearchSourceRows = 1000;
    private const int MaxSearchResultSlips = 200;

    /// <summary>
    /// 伝票検索モーダル用。入金No.・得意先コード・得意先名のいずれかにキーワードを含む
    /// 入金伝票を、伝票単位に集約したサマリで返す（新しい順）。
    /// </summary>
    public async Task<List<ReceiptHit>> SearchAsync(
        string? keyword, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Receipts.AsNoTracking().Where(r => !r.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(r =>
                r.ReceiptSlipNumber.Contains(keyword)
                || r.CustomerCode.Contains(keyword)
                || r.CustomerName.Contains(keyword));
        }

        var rows = await query
            .OrderByDescending(r => r.ReceiptDate)
            .ThenByDescending(r => r.ReceiptSlipNumber)
            .Take(MaxSearchSourceRows)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ReceiptSlipNumber)
            .OrderByDescending(g => g.Max(r => r.ReceiptDate))
            .ThenByDescending(g => g.Key)
            .Take(MaxSearchResultSlips)
            .Select(g =>
            {
                var first = g.OrderBy(r => r.LineNumber).First();
                return new ReceiptHit(
                    first.ReceiptSlipNumber,
                    first.ReceiptDate,
                    first.CustomerCode,
                    first.CustomerName,
                    first.ReceiptAmount,
                    first.AllocationStatus);
            })
            .ToList();
    }
}
