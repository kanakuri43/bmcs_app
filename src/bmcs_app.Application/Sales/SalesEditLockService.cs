using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
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
public class SalesEditLockService(BmcsDbContext dbContext)
{
    /// <summary><paramref name="lines"/> は同一伝票の明細行（1件以上）であること。</summary>
    public async Task<SalesEditLock> EvaluateAsync(
        IReadOnlyList<SalesEntity> lines, CancellationToken cancellationToken = default)
    {
        var customerCode = lines[0].CustomerCode;
        var slipDate = lines[0].SlipDate;
        var monthEndDate = new DateOnly(slipDate.Year, slipDate.Month, DateTime.DaysInMonth(slipDate.Year, slipDate.Month));

        var monthlyClosingConfirmed = await dbContext.MonthlyClosings
            .AsNoTracking()
            .AnyAsync(
                m => m.CustomerCode == customerCode
                    && m.ClosingDate == monthEndDate
                    && m.ClosingStatus == ClosingStatus.Confirmed,
                cancellationToken);

        return SalesEditLockEvaluator.Evaluate(lines, monthlyClosingConfirmed);
    }
}
