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
    // EF Core 10 は List<string>.Contains を IN (@p1, @p2, ...) に展開する（パラメータ数は
    // パディングされる）。この上限を大きく上げる場合は SQL Server のパラメータ数上限（2100）に
    // 注意し、EF.Parameter によるOPENJSON展開への切り替えを検討すること。
    private const int MaxSearchResultSlips = 200;

    /// <summary>
    /// 伝票検索モーダル用。明細入金No.・得意先コード・得意先名のいずれかにキーワードを含む
    /// 明細入金伝票を、伝票単位に集約したサマリで返す（新しい順）。
    /// </summary>
    public async Task<List<DetailReceiptHit>> SearchAsync(
        string? keyword, CancellationToken cancellationToken = default)
    {
        var baseQuery = dbContext.DetailReceipts.AsNoTracking().Where(r => !r.IsDeleted);
        var keyQuery = baseQuery;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyQuery = keyQuery.Where(r =>
                r.DetailReceiptNumber.Contains(keyword)
                || r.CustomerCode.Contains(keyword)
                || r.CustomerName.Contains(keyword));
        }

        // 上限は伝票単位に効かせる。明細行に Take を掛けると、行数の多い伝票が枠を食って
        // 新しい伝票が取りこぼされる。
        var slipKeys = await keyQuery
            .Select(r => new { r.DetailReceiptNumber, r.ReceiptDate })
            .Distinct()
            .OrderByDescending(k => k.ReceiptDate)
            .ThenByDescending(k => k.DetailReceiptNumber)
            .Take(MaxSearchResultSlips)
            .ToListAsync(cancellationToken);

        if (slipKeys.Count == 0)
        {
            return [];
        }

        var slipNumbers = slipKeys.Select(k => k.DetailReceiptNumber).ToList();

        var rows = await baseQuery
            .Where(r => slipNumbers.Contains(r.DetailReceiptNumber))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.DetailReceiptNumber)
            .OrderByDescending(g => g.Max(r => r.ReceiptDate))
            .ThenByDescending(g => g.Key)
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
