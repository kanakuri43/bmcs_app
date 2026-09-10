using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Order;

/// <summary>
/// 受注伝票の読み込み・検索ユースケース（TODO.md 5-3の共通前提）。
/// 検索は既定で中止・売上完了の受注を除外する（売上化できない受注を候補に出さないため）。
/// </summary>
public class OrderQueryService(BmcsDbContext dbContext)
{
    private const int MaxSearchSourceRows = 1000;
    private const int MaxSearchResultSlips = 200;

    /// <summary>
    /// 指定した受注伝票の全明細行を取得する（読み取り専用。追跡は不要なため <c>AsNoTracking</c>）。
    /// 論理削除された行は対象外。
    /// </summary>
    public async Task<List<OrderSlip>> GetSlipAsync(
        string orderSlipNumber, CancellationToken cancellationToken = default)
    {
        return await dbContext.OrderSlips
            .AsNoTracking()
            .Where(o => o.OrderSlipNumber == orderSlipNumber && !o.IsDeleted)
            .OrderBy(o => o.LineNumber)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 伝票検索モーダル用。伝票番号・得意先コード・得意先名のいずれかにキーワードを含む
    /// 受注伝票を、伝票単位に集約したサマリで返す（新しい順）。
    /// </summary>
    /// <param name="excludeUnavailableForSales">
    /// <c>true</c>（既定）のとき、全明細行が中止または売上完了の受注を除外する
    /// （5-3の受注No.検索では、これ以上売上化できない受注を出す意味がないため）。
    /// </param>
    public async Task<List<OrderSlipHit>> SearchAsync(
        string? keyword,
        bool excludeUnavailableForSales = true,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.OrderSlips.AsNoTracking().Where(o => !o.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(o =>
                o.OrderSlipNumber.Contains(keyword)
                || o.CustomerCode.Contains(keyword)
                || o.CustomerName.Contains(keyword));
        }

        var rows = await query
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.OrderSlipNumber)
            .Take(MaxSearchSourceRows)
            .ToListAsync(cancellationToken);

        var slips = rows
            .GroupBy(o => o.OrderSlipNumber)
            .Where(g => !excludeUnavailableForSales
                || g.Any(o => o.OrderStatus is OrderStatus.NotSold or OrderStatus.PartiallySold));

        return slips
            .OrderByDescending(g => g.Max(o => o.OrderDate))
            .ThenByDescending(g => g.Key)
            .Take(MaxSearchResultSlips)
            .Select(g =>
            {
                var first = g.OrderBy(o => o.LineNumber).First();
                return new OrderSlipHit(
                    first.OrderSlipNumber,
                    first.OrderDate,
                    first.CustomerCode,
                    first.CustomerName,
                    g.Sum(o => o.Amount),
                    first.OrderStatus);
            })
            .ToList();
    }
}
