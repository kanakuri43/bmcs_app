using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Sales;

/// <summary>
/// 売上伝票の読み込み・検索ユースケース（TODO.md 5-3・5-5・5-6の共通前提）。
/// 対象条件・並び順は <see cref="Common.ProductHistoryQueryService"/> と同じ「直近N件を取得し
/// メモリ側で加工する」方針に揃える（伝票単位への集約をSQLビューやGROUP BYで無理に組まない）。
/// </summary>
public class SalesQueryService(BmcsDbContext dbContext)
{
    // EF Core 10 は List<string>.Contains を IN (@p1, @p2, ...) に展開する（パラメータ数は
    // パディングされる）。この上限を大きく上げる場合は SQL Server のパラメータ数上限（2100）に
    // 注意し、EF.Parameter によるOPENJSON展開への切り替えを検討すること。
    private const int MaxSearchResultSlips = 200;

    /// <summary>
    /// 訂正・複写の元データとして、指定した売上伝票の全明細行を追跡ありで取得する
    /// （呼び出し元がそのまま編集・保存に使うため <c>AsNoTracking</c> は付けない）。
    /// 論理削除された行は対象外。
    /// </summary>
    public async Task<List<SalesEntity>> GetSlipAsync(
        string salesSlipNumber, CancellationToken cancellationToken = default)
    {
        return await dbContext.Sales
            .Where(s => s.SalesSlipNumber == salesSlipNumber && !s.IsDeleted)
            .OrderBy(s => s.LineNumber)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 伝票検索モーダル用。伝票番号・得意先コード・得意先名のいずれかにキーワードを含む
    /// 売上伝票を、伝票単位に集約したサマリで返す（新しい順）。
    /// </summary>
    public async Task<List<SalesSlipHit>> SearchAsync(
        string? keyword, CancellationToken cancellationToken = default)
    {
        var baseQuery = dbContext.Sales.AsNoTracking().Where(s => !s.IsDeleted);
        var keyQuery = baseQuery;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyQuery = keyQuery.Where(s =>
                s.SalesSlipNumber.Contains(keyword)
                || s.CustomerCode.Contains(keyword)
                || s.CustomerName.Contains(keyword));
        }

        // 上限は伝票単位に効かせる。明細行に Take を掛けると、行数の多い伝票が枠を食って
        // 新しい伝票が取りこぼされる。
        var slipKeys = await keyQuery
            .Select(s => new { s.SalesSlipNumber, s.SlipDate })
            .Distinct()
            .OrderByDescending(k => k.SlipDate)
            .ThenByDescending(k => k.SalesSlipNumber)
            .Take(MaxSearchResultSlips)
            .ToListAsync(cancellationToken);

        if (slipKeys.Count == 0)
        {
            return [];
        }

        var slipNumbers = slipKeys.Select(k => k.SalesSlipNumber).ToList();

        var rows = await baseQuery
            .Where(s => slipNumbers.Contains(s.SalesSlipNumber))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(s => s.SalesSlipNumber)
            .OrderByDescending(g => g.Max(s => s.SlipDate))
            .ThenByDescending(g => g.Key)
            .Select(g =>
            {
                var first = g.OrderBy(s => s.LineNumber).First();
                return new SalesSlipHit(
                    first.SalesSlipNumber,
                    first.SlipDate,
                    first.CustomerCode,
                    first.CustomerName,
                    g.Sum(s => s.Amount),
                    first.BillingStatus,
                    first.SettlementStatus);
            })
            .ToList();
    }
}
