using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Closing;

/// <summary>
/// 月次締め解除処理のユースケース（TODO.md 9-3）。指定した年月の確定済み<c>monthly_closings</c>を
/// まとめて解除済にする（<c>billings</c>と同じ非破壊方式。物理削除しない）。他のテーブルは更新しない。
/// 編集ロックは導出方式（<see cref="MonthlyClosedService"/>）のため、解除すると9-2の判定から外れ、
/// その月の伝票を再び登録・訂正・取消できる。再確定は<see cref="MonthlyClosingService.ConfirmAsync"/>
/// が解除済みの行を上書きして行う。
/// 月次締め処理（<see cref="MonthlyClosingService"/>）とは別画面として提供する（C-8・2026-09-10確定）。
/// 権限はメニュー単位の判定のみ（画面内アクション単位の権限判定は持たない。C-8）。
/// </summary>
public class MonthlyClosingReleaseService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<MonthlyClosingReleaseService> logger)
{
    /// <summary>画面の一覧表示用に、指定した年月の確定済み月次締めを読み取り専用で返す（保存しない）。</summary>
    public async Task<IReadOnlyList<MonthlyClosingReleaseTarget>> PreviewAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        var closingDate = MonthlyClosingService.MonthEnd(year, month);

        var rows = await dbContext.MonthlyClosings
            .AsNoTracking()
            .Where(m => m.ClosingDate == closingDate && !m.IsDeleted && m.ClosingStatus == ClosingStatus.Confirmed)
            .OrderBy(m => m.CustomerCode)
            .ToListAsync(cancellationToken);

        var blocked = await GetCustomersWithLaterConfirmedAsync(rows, closingDate, cancellationToken);
        return rows.Select(r => ToTarget(r, blocked.Contains(r.CustomerCode) ? BlockReasonLater : null)).ToList();
    }

    /// <summary>
    /// 指定した年月の確定済み月次締めをすべて解除する。対象のうち1件でも、より後の年月が確定済みの
    /// 得意先（解除すると後の月の前月残高とのつながりが切れる）が含まれる場合は、何も更新せずに
    /// 例外を投げる（All-or-nothing。<c>BillingReleaseService</c>と同じ）。
    /// </summary>
    /// <exception cref="MonthlyClosingException">
    /// 指定した年月に確定済みの月次締めが無い、対象の一部が解除できない、または同時に更新された場合。
    /// </exception>
    public async Task<IReadOnlyList<MonthlyClosingReleaseTarget>> ReleaseAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        var closingDate = MonthlyClosingService.MonthEnd(year, month);
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // 画面のスコープは複数回の操作をまたいで生き続けるため、前回までの追跡状態（古い状態値）を先に捨てる。
        DetachMonthlyClosings();

        var rows = await dbContext.MonthlyClosings
            .Where(m => m.ClosingDate == closingDate && !m.IsDeleted && m.ClosingStatus == ClosingStatus.Confirmed)
            .OrderBy(m => m.CustomerCode)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            throw new MonthlyClosingException($"{year}年{month:00}月の確定済みの月次締めがありません。");
        }

        var blocked = await GetCustomersWithLaterConfirmedAsync(rows, closingDate, cancellationToken);
        if (blocked.Count > 0)
        {
            throw new MonthlyClosingException(
                $"以下の得意先に、より後の年月が確定済みの月次締めがあるため、{year}年{month:00}月の解除を中止しました。\n" +
                string.Join("\n", rows.Where(r => blocked.Contains(r.CustomerCode))
                    .Select(r => $"{r.CustomerCode}: {BlockReasonLater}")));
        }

        foreach (var row in rows)
        {
            row.ClosingStatus = ClosingStatus.Released;
            row.ReleasedAt = now;
            row.ReleasedBy = employeeCode;
            row.UpdatedBy = employeeCode;
            row.UpdatedAt = now;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new MonthlyClosingException("他のユーザーがこの月次締めを更新しました。再読み込みしてください。", ex);
        }

        await transaction.CommitAsync(cancellationToken);
        var results = rows.Select(r => ToTarget(r, null)).ToList();
        DetachMonthlyClosings();

        logger.LogInformation(
            "月次締め解除を実行しました。年月={Year}/{Month} 解除件数={ReleasedCount}", year, month, results.Count);

        return results;
    }

    private const string BlockReasonLater = "より後の年月が確定済みのため解除できません。";

    /// <summary>対象行の得意先のうち、<paramref name="closingDate"/>より後に確定済みの行を持つ得意先コード。</summary>
    private async Task<HashSet<string>> GetCustomersWithLaterConfirmedAsync(
        List<MonthlyClosing> rows, DateOnly closingDate, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var customerCodes = rows.Select(r => r.CustomerCode).ToList();
        var codes = await dbContext.MonthlyClosings
            .AsNoTracking()
            .Where(m => customerCodes.Contains(m.CustomerCode)
                && m.ClosingDate > closingDate
                && m.ClosingStatus == ClosingStatus.Confirmed)
            .Select(m => m.CustomerCode)
            .Distinct()
            .ToListAsync(cancellationToken);
        return codes.ToHashSet();
    }

    /// <summary>追跡中の <see cref="MonthlyClosing"/> をすべて追跡解除する（古い状態値を次回に持ち越さない）。</summary>
    private void DetachMonthlyClosings()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<MonthlyClosing>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static MonthlyClosingReleaseTarget ToTarget(MonthlyClosing row, string? blockReason)
        => new(
            row.CustomerCode, row.CustomerName, row.TaxUnit, row.PreviousBalance, row.ReceiptAmount,
            row.SalesAmount, row.TaxAmount, row.ClosingBalance, row.ConfirmedAt, row.ConfirmedBy, blockReason);
}

/// <summary>
/// 月次締め解除処理（TODO.md 9-3）の得意先1件分。
/// </summary>
/// <param name="BlockReason">
/// 非null＝この行は解除できない。<c>BillingReleaseTarget.BlockReason</c>と同じく、1件でも非nullなら
/// 指定した年月の解除処理全体を中止する（All-or-nothing）。
/// </param>
public sealed record MonthlyClosingReleaseTarget(
    string CustomerCode,
    string CustomerName,
    TaxUnit TaxUnit,
    decimal PreviousBalance,
    decimal ReceiptAmount,
    decimal SalesAmount,
    decimal TaxAmount,
    decimal ClosingBalance,
    DateTime ConfirmedAt,
    string ConfirmedBy,
    string? BlockReason);
