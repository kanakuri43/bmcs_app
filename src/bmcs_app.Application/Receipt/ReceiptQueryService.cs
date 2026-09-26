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
    // EF Core 10 は List<string>.Contains を IN (@p1, @p2, ...) に展開する（パラメータ数は
    // パディングされる）。この上限を大きく上げる場合は SQL Server のパラメータ数上限（2100）に
    // 注意し、EF.Parameter によるOPENJSON展開への切り替えを検討すること。
    private const int MaxSearchResultSlips = 200;

    /// <summary>
    /// 伝票検索モーダル用。入金No.・得意先コード・得意先名のいずれかにキーワードを含む
    /// 入金伝票を、伝票単位に集約したサマリで返す（新しい順）。
    /// </summary>
    public async Task<List<ReceiptHit>> SearchAsync(
        string? keyword, CancellationToken cancellationToken = default)
    {
        var baseQuery = dbContext.Receipts.AsNoTracking().Where(r => !r.IsDeleted);
        var keyQuery = baseQuery;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyQuery = keyQuery.Where(r =>
                r.ReceiptSlipNumber.Contains(keyword)
                || r.CustomerCode.Contains(keyword)
                || r.CustomerName.Contains(keyword));
        }

        // 上限は伝票単位に効かせる。明細行に Take を掛けると、行数の多い伝票が枠を食って
        // 新しい伝票が取りこぼされる。
        var slipKeys = await keyQuery
            .Select(r => new { r.ReceiptSlipNumber, r.ReceiptDate })
            .Distinct()
            .OrderByDescending(k => k.ReceiptDate)
            .ThenByDescending(k => k.ReceiptSlipNumber)
            .Take(MaxSearchResultSlips)
            .ToListAsync(cancellationToken);

        if (slipKeys.Count == 0)
        {
            return [];
        }

        var slipNumbers = slipKeys.Select(k => k.ReceiptSlipNumber).ToList();

        var rows = await baseQuery
            .Where(r => slipNumbers.Contains(r.ReceiptSlipNumber))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ReceiptSlipNumber)
            .OrderByDescending(g => g.Max(r => r.ReceiptDate))
            .ThenByDescending(g => g.Key)
            .Select(g =>
            {
                var first = g.OrderBy(r => r.LineNumber).First();
                return new ReceiptHit(
                    first.ReceiptSlipNumber,
                    first.ReceiptDate,
                    first.CustomerCode,
                    first.CustomerName,
                    g.Sum(r => r.Amount),
                    first.AllocationStatus);
            })
            .ToList();
    }
}
