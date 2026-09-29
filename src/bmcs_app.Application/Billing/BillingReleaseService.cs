using bmcs_app.Application.Common;
using bmcs_app.Application.Receipt;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BillingEntity = bmcs_app.Domain.Entities.Billing;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 締め解除処理のユースケース（TODO.md 6-2、2026-09-15改訂）。指定した請求日
/// （<c>billing.billing_date</c>）の確定済み<c>billing</c>をまとめて解除済にし、
/// 紐付いていた<c>sales</c>行を未請求へ戻す。請求締め処理（<see cref="BillingClosingService"/>）
/// が「締め日を指定して一括」処理するのと粒度を揃える（請求番号を1件ずつ指定する方式から変更）。
/// 請求締め処理とは別画面（別ウィンドウ）として提供する（C-8・2026-09-10確定）。
/// </summary>
public class BillingReleaseService(
    BmcsDbContext dbContext,
    SettlementService settlementService,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<BillingReleaseService> logger)
{
    /// <summary>画面での一覧表示用に、指定した請求日の確定済み請求データを読み取り専用で返す（保存しない）。</summary>
    public async Task<IReadOnlyList<BillingReleaseTarget>> PreviewAsync(
        DateOnly billingDate, CancellationToken cancellationToken = default)
    {
        var targets = await dbContext.Billings
            .AsNoTracking()
            .Where(b => b.BillingDate == billingDate && !b.IsDeleted && b.BillingStatus == BillingStatus.Confirmed)
            .OrderBy(b => b.CustomerCode)
            .ToListAsync(cancellationToken);

        var blockReasons = await DetermineBlockReasonsAsync(targets, cancellationToken);
        return targets.Select(b => ToTarget(b, blockReasons[b.BillingNumber])).ToList();
    }

    /// <summary>
    /// 指定した請求日の確定済み<c>billing</c>をすべて解除する。対象のうち1件でも
    /// 締め順序が逆転する（より新しい確定済み<c>billing</c>が存在する）ものが含まれる場合は、
    /// 何も更新せずに例外を投げる（All-or-nothing。2026-09-15ユーザー確認）。
    /// </summary>
    /// <exception cref="BillingReleaseException">
    /// 指定した請求日に確定済み<c>billing</c>が存在しない、または対象の一部が
    /// 締め順序の逆転により解除できない場合。
    /// </exception>
    public async Task<IReadOnlyList<BillingReleaseTarget>> ReleaseByBillingDateAsync(
        DateOnly billingDate, CancellationToken cancellationToken = default)
    {
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var targets = await dbContext.Billings
            .Where(b => b.BillingDate == billingDate && !b.IsDeleted && b.BillingStatus == BillingStatus.Confirmed)
            .ToListAsync(cancellationToken);

        if (targets.Count == 0)
        {
            throw new BillingReleaseException(
                $"請求日 {billingDate:yyyy/MM/dd} の確定済み請求データがありません。");
        }

        var blockReasons = await DetermineBlockReasonsAsync(targets, cancellationToken);
        var blocked = targets
            .Where(b => blockReasons[b.BillingNumber] is not null)
            .Select(b => $"{b.CustomerCode}（{b.BillingNumber}）: {blockReasons[b.BillingNumber]}")
            .ToList();
        if (blocked.Count > 0)
        {
            throw new BillingReleaseException(
                $"以下の請求データが解除できないため、請求日 {billingDate:yyyy/MM/dd} の解除を中止しました。\n" +
                string.Join("\n", blocked));
        }

        foreach (var billing in targets)
        {
            billing.BillingStatus = BillingStatus.Released;
            billing.ReleasedAt = now;
            billing.ReleasedBy = employeeCode;
            billing.UpdatedBy = employeeCode;
            billing.UpdatedAt = now;
        }

        var billingNumbers = targets.Select(b => b.BillingNumber).ToList();
        var salesLines = await dbContext.Sales
            .Where(s => s.BillingNumber != null && billingNumbers.Contains(s.BillingNumber) && !s.IsDeleted)
            .ToListAsync(cancellationToken);
        foreach (var line in salesLines)
        {
            line.BillingNumber = null;
            line.BillingStatus = BillingLinkStatus.Unbilled;
            line.UpdatedBy = employeeCode;
            line.UpdatedAt = now;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new BillingReleaseException(
                "他のユーザーがこの請求データを更新しました。再読み込みしてください。", ex);
        }

        // 解除で billing_number が外れた売上行は充当先を失うため、消込キャッシュ列を未消込へ
        // 戻す（TODO.md 7-1。解除前は消込完了/一部消込のまま取り残されていた既存の不整合の修正）。
        // 解除した請求に充当されていた入金（receipt_allocation.allocated_amount）自体は
        // 本処理では付け替えない（再消込は7-2の責務。docs/design_document.md 16章「既知の限界」）。
        foreach (var customerCode in targets.Select(b => b.CustomerCode).Distinct())
        {
            await settlementService.RecalculateForBillingGroupAsync(customerCode, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "締め解除を実行しました。請求日={BillingDate} 解除件数={ReleasedCount} 対象売上行数={SalesLineCount}",
            billingDate, targets.Count, salesLines.Count);

        return targets.Select(b => ToTarget(b, null)).ToList();
    }

    /// <summary>
    /// 対象<c>billing</c>ごとに、締め順序の逆転（同一得意先の確定済み<c>billing</c>のうち
    /// 締め年月が最も新しいものでない）を判定する。<see cref="BillingClosingService.BuildCandidateAsync"/>
    /// と対称の制約を、対象得意先分まとめて1クエリで判定する（N+1回避）。
    /// </summary>
    private async Task<Dictionary<string, string?>> DetermineBlockReasonsAsync(
        List<BillingEntity> targets, CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return [];
        }

        var customerCodes = targets.Select(b => b.CustomerCode).Distinct().ToList();
        var confirmedByCustomer = await dbContext.Billings
            .AsNoTracking()
            .Where(b => customerCodes.Contains(b.CustomerCode)
                && !b.IsDeleted
                && b.BillingStatus == BillingStatus.Confirmed)
            .ToListAsync(cancellationToken);
        var latestConfirmedByCustomer = confirmedByCustomer
            .GroupBy(b => b.CustomerCode)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.ClosingYearMonth).First());

        return targets.ToDictionary(
            b => b.BillingNumber,
            b =>
            {
                var latestConfirmed = latestConfirmedByCustomer[b.CustomerCode];
                return latestConfirmed.BillingNumber != b.BillingNumber
                    ? $"より新しい請求データ（{latestConfirmed.BillingNumber}／{latestConfirmed.ClosingYearMonth}）が" +
                      "確定済みのため解除できません。先にそちらを解除してください。"
                    : null;
            });
    }

    private static BillingReleaseTarget ToTarget(BillingEntity billing, string? blockReason) => new(
        billing.BillingNumber,
        billing.CustomerCode,
        billing.CustomerName,
        billing.TaxUnit,
        billing.ClosingYearMonth,
        billing.PreviousBalance,
        billing.ReceiptAmount,
        billing.SalesAmount,
        billing.TaxAmount,
        billing.CurrentBillingAmount,
        billing.ConfirmedAt,
        billing.ConfirmedBy,
        blockReason);
}

/// <summary>締め解除処理の業務ルール違反。</summary>
public sealed class BillingReleaseException(string message, Exception? inner = null) : Exception(message, inner);
