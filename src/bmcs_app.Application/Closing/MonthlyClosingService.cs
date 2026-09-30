using bmcs_app.Application.Common;
using bmcs_app.Application.Ledger;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Closing;

/// <summary>
/// 月次締め処理のユースケース（TODO.md 9-1）。全得意先の暦月末売掛残高を <c>monthly_closings</c> に
/// 確定保存する。金額の計算は <see cref="MonthlyClosingCalculator"/>（Domain）、元帳の入力は
/// <see cref="CustomerLedgerQueryService"/> と共通（集計値を元帳の残高と一致させるため）。
/// 集計・スキップ規則の詳細は docs/design_document.md の月次締めの章を参照。
/// </summary>
public class MonthlyClosingService(
    BmcsDbContext dbContext,
    CustomerLedgerQueryService ledgerQueryService,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<MonthlyClosingService> logger)
{
    /// <summary>年月の月末日（<c>monthly_closings.closing_date</c>）。</summary>
    public static DateOnly MonthEnd(int year, int month)
        => new(year, month, DateTime.DaysInMonth(year, month));

    /// <summary>
    /// 指定した年月（暦月）を全得意先について確定する。独自のトランザクションを開くため、
    /// 呼び出し側が既にトランザクションを開いていると EF が例外を投げる（<c>BillingClosingService</c>と同じ）。
    /// </summary>
    public async Task<IReadOnlyList<MonthlyClosingTarget>> ConfirmAsync(
        int year, int month, CancellationToken cancellationToken = default)
    {
        var closingDate = MonthEnd(year, month);
        var periodFrom = new DateOnly(year, month, 1);
        var previousClosingDate = periodFrom.AddDays(-1);
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var customers = await dbContext.Customers.AsNoTracking()
            .OrderBy(c => c.CustomerCode)
            .ToListAsync(cancellationToken);

        // 当月の行は上書き（解除済みの再確定）のため追跡して読む。画面のスコープは複数回の確定・
        // 他画面での解除をまたいで生き続けるため、前回までの追跡状態（古い状態値）を先に捨てる。
        DetachMonthlyClosings();
        var currentRows = await dbContext.MonthlyClosings
            .Where(m => m.ClosingDate == closingDate)
            .ToDictionaryAsync(m => m.CustomerCode, cancellationToken);
        var previousRows = await dbContext.MonthlyClosings.AsNoTracking()
            .Where(m => m.ClosingDate == previousClosingDate)
            .ToDictionaryAsync(m => m.CustomerCode, cancellationToken);
        var laterConfirmedCodes = (await dbContext.MonthlyClosings.AsNoTracking()
                .Where(m => m.ClosingDate > closingDate && m.ClosingStatus == ClosingStatus.Confirmed)
                .Select(m => m.CustomerCode)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var results = new List<MonthlyClosingTarget>();
        var overwrittenCount = 0;

        foreach (var customer in customers)
        {
            currentRows.TryGetValue(customer.CustomerCode, out var currentRow);
            previousRows.TryGetValue(customer.CustomerCode, out var previousRow);

            string? skipReason = null;
            if (currentRow is { ClosingStatus: ClosingStatus.Confirmed })
            {
                skipReason = "この年月は確定済みです。";
            }
            else if (laterConfirmedCodes.Contains(customer.CustomerCode))
            {
                skipReason = "より後の年月が確定済みのため締められません。";
            }
            else if (previousRow is { ClosingStatus: ClosingStatus.Released })
            {
                skipReason = "前月の月次締めが解除されています。前月を再確定してください。";
            }

            if (skipReason is not null)
            {
                results.Add(SkippedTarget(customer, skipReason));
                continue;
            }

            var input = await ledgerQueryService.GetInputAsync(
                customer.CustomerCode, periodFrom, closingDate, cancellationToken);
            var ledger = CustomerLedgerBuilder.Build(input!);

            MonthlyClosingAmounts amounts;
            try
            {
                amounts = MonthlyClosingCalculator.Calculate(input!, ledger, previousRow?.ClosingBalance);
            }
            catch (InvalidOperationException ex)
            {
                throw new MonthlyClosingException(ex.Message, ex);
            }

            if (amounts.IsEmpty)
            {
                results.Add(SkippedTarget(customer, "前月残高・売上・入金がないため対象外です。"));
                continue;
            }

            if (currentRow is null)
            {
                currentRow = new MonthlyClosing
                {
                    ClosingDate = closingDate,
                    CustomerCode = customer.CustomerCode,
                    TaxUnit = customer.TaxUnit,
                    CustomerName = customer.CustomerName,
                    PreviousBalance = amounts.PreviousBalance,
                    SalesAmount = amounts.SalesAmount,
                    ReceiptAmount = amounts.ReceiptAmount,
                    TaxAmount = amounts.TaxAmount,
                    ClosingBalance = amounts.ClosingBalance,
                    StandardRateTaxableAmount = amounts.Breakdown.StandardRateTaxableAmount,
                    StandardRateTaxAmount = amounts.Breakdown.StandardRateTaxAmount,
                    ReducedRateTaxableAmount = amounts.Breakdown.ReducedRateTaxableAmount,
                    ReducedRateTaxAmount = amounts.Breakdown.ReducedRateTaxAmount,
                    TaxExemptAmount = amounts.Breakdown.TaxExemptAmount,
                    ClosingStatus = ClosingStatus.Confirmed,
                    ConfirmedAt = now,
                    ConfirmedBy = employeeCode,
                    CreatedBy = employeeCode,
                    CreatedAt = now,
                    UpdatedBy = employeeCode,
                    UpdatedAt = now,
                };
                dbContext.MonthlyClosings.Add(currentRow);
            }
            else
            {
                // 解除済みの行は主キー（closing_date, customer_code）が同じため新規行を作れない。
                // 既存行を今回の集計値で上書きして確定に戻す。
                currentRow.TaxUnit = customer.TaxUnit;
                currentRow.CustomerName = customer.CustomerName;
                currentRow.PreviousBalance = amounts.PreviousBalance;
                currentRow.SalesAmount = amounts.SalesAmount;
                currentRow.ReceiptAmount = amounts.ReceiptAmount;
                currentRow.TaxAmount = amounts.TaxAmount;
                currentRow.ClosingBalance = amounts.ClosingBalance;
                currentRow.StandardRateTaxableAmount = amounts.Breakdown.StandardRateTaxableAmount;
                currentRow.StandardRateTaxAmount = amounts.Breakdown.StandardRateTaxAmount;
                currentRow.ReducedRateTaxableAmount = amounts.Breakdown.ReducedRateTaxableAmount;
                currentRow.ReducedRateTaxAmount = amounts.Breakdown.ReducedRateTaxAmount;
                currentRow.TaxExemptAmount = amounts.Breakdown.TaxExemptAmount;
                currentRow.ClosingStatus = ClosingStatus.Confirmed;
                currentRow.ConfirmedAt = now;
                currentRow.ConfirmedBy = employeeCode;
                currentRow.ReleasedAt = null;
                currentRow.ReleasedBy = null;
                currentRow.UpdatedBy = employeeCode;
                currentRow.UpdatedAt = now;
                overwrittenCount++;
            }

            results.Add(new MonthlyClosingTarget(
                customer.CustomerCode, customer.CustomerName, customer.TaxUnit,
                amounts.PreviousBalance, amounts.SalesAmount, amounts.ReceiptAmount,
                amounts.TaxAmount, amounts.ClosingBalance, null));
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new MonthlyClosingException("他のユーザーが同じ月次締めを更新しました。再読み込みしてください。", ex);
        }
        catch (DbUpdateException ex)
        {
            throw new MonthlyClosingException("他のユーザーが同時に同じ年月を締めました。再読み込みしてください。", ex);
        }

        await transaction.CommitAsync(cancellationToken);
        DetachMonthlyClosings();

        logger.LogInformation(
            "月次締めを実行しました。年月={Year}/{Month} 確定件数={ConfirmedCount}（うち解除済みの再確定={OverwrittenCount}） スキップ件数={SkippedCount}",
            year, month,
            results.Count(r => r.SkipReason is null), overwrittenCount,
            results.Count(r => r.SkipReason is not null));

        return results;
    }

    /// <summary>追跡中の <see cref="MonthlyClosing"/> をすべて追跡解除する（古い状態値を次回に持ち越さない）。</summary>
    private void DetachMonthlyClosings()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<MonthlyClosing>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static MonthlyClosingTarget SkippedTarget(Customer customer, string reason)
        => new(customer.CustomerCode, customer.CustomerName, customer.TaxUnit, 0m, 0m, 0m, 0m, 0m, reason);
}

/// <summary>月次締め処理の業務ルール違反（データ異常・同時実行の検出）。</summary>
public sealed class MonthlyClosingException(string message, Exception? inner = null) : Exception(message, inner);
