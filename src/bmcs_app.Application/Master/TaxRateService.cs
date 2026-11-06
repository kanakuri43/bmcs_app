using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>
/// 消費税率マスタ（tax_rates）のユースケース。
/// 行の適用期間は「施行日 〜 次に新しい有効行の施行日の前日（最新行は無期限）」。
/// 受注・売上伝票がある期間の税率は、あとから変わると同じ日付の伝票に別の税率が混ざるため、
/// 変更・削除・割り込み追加を認めない。施行日は主キーのため変更できない（削除して追加し直す）。
/// </summary>
public class TaxRateService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<TaxRateService> logger)
{
    public async Task<TaxRateMaster> CreateAsync(
        DateOnly effectiveDate, decimal standardTaxRate, decimal reducedTaxRate,
        CancellationToken cancellationToken = default)
    {
        ValidateRates(standardTaxRate, reducedTaxRate);

        var existing = await dbContext.TaxRateMasters
            .SingleOrDefaultAsync(m => m.EffectiveDate == effectiveDate, cancellationToken);
        if (existing is { IsDeleted: false })
        {
            throw new TaxRateValidationException($"施行日 {effectiveDate:yyyy/MM/dd} の税率は既に登録されています。");
        }

        // 追加した行が受け持つ期間は、次に新しい有効行の前日まで。
        var next = await NextEffectiveDateAsync(effectiveDate, cancellationToken);
        await EnsureNoSlipsAsync(effectiveDate, next, "追加", cancellationToken);

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        if (existing is null)
        {
            existing = new TaxRateMaster
            {
                EffectiveDate = effectiveDate,
                StandardTaxRate = standardTaxRate,
                ReducedTaxRate = reducedTaxRate,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
            };
            dbContext.TaxRateMasters.Add(existing);
        }
        else
        {
            // 主キーが施行日のため、削除済みの同日行は新規挿入できない。復活させる。
            existing.IsDeleted = false;
            existing.StandardTaxRate = standardTaxRate;
            existing.ReducedTaxRate = reducedTaxRate;
            existing.UpdatedBy = employeeCode;
            existing.UpdatedAt = now;
        }

        await SaveAsync(cancellationToken);
        logger.LogInformation("消費税率を登録しました。EffectiveDate={EffectiveDate}", effectiveDate);
        return existing;
    }

    public async Task<TaxRateMaster> UpdateAsync(
        DateOnly effectiveDate, decimal standardTaxRate, decimal reducedTaxRate, byte[] rowVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateRates(standardTaxRate, reducedTaxRate);

        var current = await GetActiveAsync(effectiveDate, rowVersion, cancellationToken);
        await EnsureNoSlipsAsync(
            effectiveDate, await NextEffectiveDateAsync(effectiveDate, cancellationToken), "変更", cancellationToken);

        current.StandardTaxRate = standardTaxRate;
        current.ReducedTaxRate = reducedTaxRate;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        await SaveAsync(cancellationToken);
        logger.LogInformation("消費税率を更新しました。EffectiveDate={EffectiveDate}", effectiveDate);
        return current;
    }

    public async Task DeactivateAsync(
        DateOnly effectiveDate, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        var current = await GetActiveAsync(effectiveDate, rowVersion, cancellationToken);

        // 0件になると受注入力・売上入力で税率を解決できなくなる。
        if (await dbContext.TaxRateMasters.CountAsync(m => !m.IsDeleted, cancellationToken) <= 1)
        {
            throw new TaxRateValidationException("最後の1件の税率は削除できません。");
        }

        await EnsureNoSlipsAsync(
            effectiveDate, await NextEffectiveDateAsync(effectiveDate, cancellationToken), "削除", cancellationToken);

        current.IsDeleted = true;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        await SaveAsync(cancellationToken);
        logger.LogInformation("消費税率を削除しました。EffectiveDate={EffectiveDate}", effectiveDate);
    }

    private async Task<TaxRateMaster> GetActiveAsync(
        DateOnly effectiveDate, byte[] rowVersion, CancellationToken cancellationToken)
    {
        var current = await dbContext.TaxRateMasters
            .SingleOrDefaultAsync(m => m.EffectiveDate == effectiveDate && !m.IsDeleted, cancellationToken)
            ?? throw new TaxRateValidationException($"施行日 {effectiveDate:yyyy/MM/dd} の税率は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(rowVersion))
        {
            throw new TaxRateConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        return current;
    }

    /// <summary>指定日より新しい有効行のうち最も古い施行日。なければ null（無期限）。</summary>
    private async Task<DateOnly?> NextEffectiveDateAsync(DateOnly effectiveDate, CancellationToken cancellationToken)
        => await dbContext.TaxRateMasters
            .Where(m => !m.IsDeleted && m.EffectiveDate > effectiveDate)
            .OrderBy(m => m.EffectiveDate)
            .Select(m => (DateOnly?)m.EffectiveDate)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>期間 [from, toExclusive) に受注・売上伝票があれば拒否する。</summary>
    private async Task EnsureNoSlipsAsync(
        DateOnly from, DateOnly? toExclusive, string operation, CancellationToken cancellationToken)
    {
        var hasSales = await dbContext.Sales.AnyAsync(
            s => !s.IsDeleted && s.SlipDate >= from && (toExclusive == null || s.SlipDate < toExclusive),
            cancellationToken);
        var hasOrders = hasSales || await dbContext.OrderSlips.AnyAsync(
            o => !o.IsDeleted && o.OrderDate >= from && (toExclusive == null || o.OrderDate < toExclusive),
            cancellationToken);

        if (hasOrders)
        {
            var range = toExclusive is { } to
                ? $"{from:yyyy/MM/dd}〜{to.AddDays(-1):yyyy/MM/dd}"
                : $"{from:yyyy/MM/dd}以降";
            throw new TaxRateValidationException(
                $"{range} に受注または売上の伝票があるため、この税率は{operation}できません。");
        }
    }

    /// <summary>税率は0以上100未満・小数2桁まで（decimal(5,2)）。</summary>
    private static void ValidateRates(decimal standardTaxRate, decimal reducedTaxRate)
    {
        foreach (var (label, rate) in new[] { ("標準税率", standardTaxRate), ("軽減税率", reducedTaxRate) })
        {
            if (rate < 0 || rate >= 100)
            {
                throw new TaxRateValidationException($"{label}は0以上100未満で入力してください。");
            }

            if (decimal.Round(rate, 2) != rate)
            {
                throw new TaxRateValidationException($"{label}は小数2桁までで入力してください。");
            }
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new TaxRateConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }
    }
}

/// <summary>消費税率マスタの業務ルール違反。</summary>
public sealed class TaxRateValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class TaxRateConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
