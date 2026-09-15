using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Receipt;

/// <summary>
/// 明細入金の検索ユースケース（伝票検索モーダル用）。<see cref="DetailReceiptEntryService"/> は
/// 登録・照会のコマンドサービスのため、読み取り専用の検索はこちらへ分離する
/// （<see cref="ReceiptQueryService"/> と対称）。
/// </summary>
public class DetailReceiptQueryService(BmcsDbContext dbContext)
{
    private const int MaxSearchSourceRows = 1000;
    private const int MaxSearchResultSlips = 200;

    /// <summary>
    /// 伝票検索モーダル用。明細入金No.・得意先コード・得意先名のいずれかにキーワードを含む
    /// 明細入金伝票を、伝票単位に集約したサマリで返す（新しい順）。
    /// </summary>
    public async Task<List<DetailReceiptHit>> SearchAsync(
        string? keyword, CancellationToken cancellationToken = default)
    {
        var query = dbContext.DetailReceipts.AsNoTracking().Where(r => !r.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(r =>
                r.DetailReceiptNumber.Contains(keyword)
                || r.CustomerCode.Contains(keyword)
                || r.CustomerName.Contains(keyword));
        }

        var rows = await query
            .OrderByDescending(r => r.ReceiptDate)
            .ThenByDescending(r => r.DetailReceiptNumber)
            .Take(MaxSearchSourceRows)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.DetailReceiptNumber)
            .OrderByDescending(g => g.Max(r => r.ReceiptDate))
            .ThenByDescending(g => g.Key)
            .Take(MaxSearchResultSlips)
            .Select(g =>
            {
                var first = g.OrderBy(r => r.LineNumber).First();
                return new DetailReceiptHit(
                    first.DetailReceiptNumber,
                    first.ReceiptDate,
                    first.CustomerCode,
                    first.CustomerName,
                    first.ReceiptAmount,
                    first.AllocationStatus);
            })
            .ToList();
    }
}

/// <summary>伝票検索モーダル用の明細入金サマリ。</summary>
public sealed record DetailReceiptHit(
    string DetailReceiptNumber,
    DateOnly ReceiptDate,
    string CustomerCode,
    string CustomerName,
    decimal ReceiptAmount,
    AllocationStatus AllocationStatus);
