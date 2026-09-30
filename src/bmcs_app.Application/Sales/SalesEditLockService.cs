using bmcs_app.Application.Closing;
using bmcs_app.Domain.Calculations;
using bmcs_app.Infrastructure;
using SalesEntity = bmcs_app.Domain.Entities.Sales;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Sales;

/// <summary>
/// 売上伝票の編集可否（C-6の3条件）を判定するユースケース（TODO.md 5-6）。
/// DBアクセス（<c>monthly_closing</c> の照会）はここで行い、判定ロジック自体は
/// <see cref="SalesEditLockEvaluator"/>（Domain の純粋関数）に委ねる。
/// ViewModel は業務ルールを判断せず、本サービスの結果をそのまま表示するだけにする
/// （docs/architecture.md 5章）。
/// </summary>
public class SalesEditLockService(BmcsDbContext dbContext, MonthlyClosedService monthlyClosedService)
{
    /// <summary><paramref name="lines"/> は同一伝票の明細行（1件以上）であること。</summary>
    public async Task<SalesEditLock> EvaluateAsync(
        IReadOnlyList<SalesEntity> lines, CancellationToken cancellationToken = default)
    {
        var customerCode = lines[0].CustomerCode;
        var slipDate = lines[0].SlipDate;

        var monthlyClosingConfirmed = await monthlyClosedService.IsClosedAsync(customerCode, slipDate, cancellationToken);

        var slipNumber = lines[0].SalesSlipNumber;
        var lineNumbers = lines.Select(l => l.LineNumber).ToList();
        var detailInvoiceLinked = await dbContext.DetailInvoiceSalesLines
            .AsNoTracking()
            .AnyAsync(
                l => l.SalesSlipNumber == slipNumber && lineNumbers.Contains(l.SalesLineNumber),
                cancellationToken);

        return SalesEditLockEvaluator.Evaluate(lines, monthlyClosingConfirmed, detailInvoiceLinked);
    }
}
