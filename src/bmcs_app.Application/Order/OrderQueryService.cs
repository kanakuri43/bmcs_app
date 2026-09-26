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
    // EF Core 10 は List<string>.Contains を IN (@p1, @p2, ...) に展開する（パラメータ数は
    // パディングされる）。この上限を大きく上げる場合は SQL Server のパラメータ数上限（2100）に
    // 注意し、EF.Parameter によるOPENJSON展開への切り替えを検討すること。
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
        var baseQuery = dbContext.OrderSlips.AsNoTracking().Where(o => !o.IsDeleted);
        var keyQuery = baseQuery;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyQuery = keyQuery.Where(o =>
                o.OrderSlipNumber.Contains(keyword)
                || o.CustomerCode.Contains(keyword)
                || o.CustomerName.Contains(keyword));
        }

        if (excludeUnavailableForSales)
        {
            // 売上化余地のある行を1つでも持つ受注だけを候補にする。除外判定を伝票キー確定の
            // 後段に置くと、除外対象がSQL側の枠を占有して有効な受注が取りこぼされる。
            var availableSlipNumbers = keyQuery
                .Where(o => o.OrderStatus == OrderStatus.NotSold || o.OrderStatus == OrderStatus.PartiallySold)
                .Select(o => o.OrderSlipNumber);

            keyQuery = keyQuery.Where(o => availableSlipNumbers.Contains(o.OrderSlipNumber));
        }

        // 上限は伝票単位に効かせる。明細行に Take を掛けると、行数の多い伝票が枠を食って
        // 新しい伝票が取りこぼされる。
        var slipKeys = await keyQuery
            .Select(o => new { o.OrderSlipNumber, o.OrderDate })
            .Distinct()
            .OrderByDescending(k => k.OrderDate)
            .ThenByDescending(k => k.OrderSlipNumber)
            .Take(MaxSearchResultSlips)
            .ToListAsync(cancellationToken);

        if (slipKeys.Count == 0)
        {
            return [];
        }

        var slipNumbers = slipKeys.Select(k => k.OrderSlipNumber).ToList();

        var rows = await baseQuery
            .Where(o => slipNumbers.Contains(o.OrderSlipNumber))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(o => o.OrderSlipNumber)
            .OrderByDescending(g => g.Max(o => o.OrderDate))
            .ThenByDescending(g => g.Key)
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
