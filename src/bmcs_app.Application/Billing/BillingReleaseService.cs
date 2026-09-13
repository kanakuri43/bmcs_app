using bmcs_app.Application.Common;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BillingEntity = bmcs_app.Domain.Entities.Billing;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 締め解除処理のユースケース（TODO.md 6-2）。確定済み<c>billing</c>を解除済にし、
/// 紐付いていた<c>sales</c>行を未請求へ戻す。請求締め処理（<see cref="BillingClosingService"/>）
/// とは別画面（別ウィンドウ）として提供する（C-8・2026-09-10確定）。
/// </summary>
public class BillingReleaseService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<BillingReleaseService> logger)
{
    /// <summary>画面での表示用に、請求番号で1件取得する（保存しない）。</summary>
    public Task<BillingEntity?> GetByNumberAsync(string billingNumber, CancellationToken cancellationToken = default)
        => dbContext.Billings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BillingNumber == billingNumber && !b.IsDeleted, cancellationToken);

    /// <summary>
    /// 指定した請求データを解除する。解除できるのは、同一得意先の確定済み<c>billing</c>のうち
    /// 締め年月が最も新しいもの（＝前回残高の引継ぎ元）に限る。それより古いものを解除すると、
    /// より新しい確定済み<c>billing</c>が参照している前回残高が不整合になるため
    /// （<see cref="BillingClosingService"/>の<c>previousBalance</c>算出と対で守る制約）。
    /// </summary>
    /// <exception cref="BillingReleaseException">
    /// 請求データが存在しない、既に解除済み、またはより新しい確定済み<c>billing</c>が存在する場合。
    /// </exception>
    public async Task<BillingEntity> ReleaseAsync(string billingNumber, CancellationToken cancellationToken = default)
    {
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var billing = await dbContext.Billings
            .FirstOrDefaultAsync(b => b.BillingNumber == billingNumber && !b.IsDeleted, cancellationToken);
        if (billing is null)
        {
            throw new BillingReleaseException($"請求データが見つかりません。BillingNumber={billingNumber}");
        }

        if (billing.BillingStatus == BillingStatus.Released)
        {
            throw new BillingReleaseException("既に解除済みです。");
        }

        // BillingClosingService.BuildCandidateAsync と同じ「同一得意先の確定済みbillingで
        // 締め年月が最も新しいもの」を求め、それが自分自身でなければ締め順序が逆転する。
        var latestConfirmed = await dbContext.Billings
            .AsNoTracking()
            .Where(b => b.CustomerCode == billing.CustomerCode
                && !b.IsDeleted
                && b.BillingStatus == BillingStatus.Confirmed)
            .OrderByDescending(b => b.ClosingYearMonth)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestConfirmed is not null && latestConfirmed.BillingNumber != billing.BillingNumber)
        {
            throw new BillingReleaseException(
                $"より新しい請求データ（{latestConfirmed.BillingNumber}／{latestConfirmed.ClosingYearMonth}）が" +
                "確定済みのため解除できません。先にそちらを解除してください。");
        }

        billing.BillingStatus = BillingStatus.Released;
        billing.ReleasedAt = now;
        billing.ReleasedBy = employeeCode;
        billing.UpdatedBy = employeeCode;
        billing.UpdatedAt = now;

        var salesLines = await dbContext.Sales
            .Where(s => s.BillingNumber == billingNumber && !s.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var line in salesLines)
        {
            line.BillingNumber = null;
            line.BillingStatus = BillingLinkStatus.Unbilled;
            line.UpdatedBy = employeeCode;
            line.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "締め解除を実行しました。BillingNumber={BillingNumber} 対象売上行数={SalesLineCount}",
            billingNumber, salesLines.Count);

        return billing;
    }
}

/// <summary>締め解除処理の業務ルール違反。</summary>
public sealed class BillingReleaseException(string message) : Exception(message);
