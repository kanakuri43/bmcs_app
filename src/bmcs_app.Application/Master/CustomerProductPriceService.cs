using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>
/// 得意先／商品／単価・掛率マスタのユースケース。得意先1件分の単価と定価掛率を商品の一覧として読み込み、
/// 変更した行をまとめて（1回の SaveChanges で）登録する。
/// </summary>
public class CustomerProductPriceService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<CustomerProductPriceService> logger)
{
    /// <summary>decimal(15,4) に収まる最大値。</summary>
    private const decimal MaxUnitPrice = 99_999_999_999.9999m;

    private const int MinRate = 0;

    private const int MaxRate = 100;

    /// <summary>
    /// 有効な商品の全件に、得意先の単価・掛率を付けて返す。無効化済みの行の値は null（未登録扱い）で、
    /// RowVersion だけ持たせる（登録時の競合検出と、無効化済みの行の復活に使う）。
    /// </summary>
    public async Task<IReadOnlyList<CustomerProductSetting>> GetSettingsAsync(
        string customerCode, CancellationToken cancellationToken = default)
    {
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.ProductCode)
            .Select(p => new { p.ProductCode, p.ProductName, p.CatalogPrice })
            .ToListAsync(cancellationToken);
        var prices = await dbContext.CustomerProductPrices
            .AsNoTracking()
            .Where(p => p.CustomerCode == customerCode)
            .ToDictionaryAsync(p => p.ProductCode, cancellationToken);
        var rates = await dbContext.CustomerProductRates
            .AsNoTracking()
            .Where(r => r.CustomerCode == customerCode)
            .ToDictionaryAsync(r => r.ProductCode, cancellationToken);

        return products.Select(p =>
        {
            prices.TryGetValue(p.ProductCode, out var price);
            rates.TryGetValue(p.ProductCode, out var rate);
            return new CustomerProductSetting(
                p.ProductCode,
                p.ProductName,
                p.CatalogPrice,
                price is { IsDeleted: false } ? price.UnitPrice : null,
                price?.RowVersion,
                rate is { IsDeleted: false } ? rate.CatalogPriceRate : null,
                rate?.RowVersion);
        }).ToList();
    }

    /// <summary>
    /// 変更した行をまとめて登録する（全件成功か全件失敗）。値が <c>null</c> の変更は、登録済みなら無効化、未登録なら何もしない。
    /// 値がある変更は、行が無ければ追加、あれば更新し、無効化済みの行は有効に戻す。
    /// </summary>
    public async Task SaveSettingsAsync(
        string customerCode,
        IReadOnlyList<PriceChange> priceChanges,
        IReadOnlyList<RateChange> rateChanges,
        CancellationToken cancellationToken = default)
    {
        foreach (var change in priceChanges)
        {
            if (change.UnitPrice is { } unitPrice && (unitPrice < 0m || unitPrice > MaxUnitPrice))
            {
                // 返品はマイナス数量で表すため、単価自体は 0 以上とする。
                throw new CustomerProductPriceValidationException(
                    $"商品「{change.ProductCode}」: 単価は 0 以上 {MaxUnitPrice:N4} 以下で入力してください。");
            }
        }

        foreach (var change in rateChanges)
        {
            if (change.Rate is { } rate && (rate < MinRate || rate > MaxRate))
            {
                // 定価を超える掛率の運用はない。
                throw new CustomerProductPriceValidationException(
                    $"商品「{change.ProductCode}」: 定価掛率は {MinRate} 〜 {MaxRate} の整数で入力してください。");
            }
        }

        try
        {
            await EnsureActiveCustomerAndProductsAsync(
                customerCode,
                priceChanges.Where(c => c.UnitPrice is not null).Select(c => c.ProductCode)
                    .Concat(rateChanges.Where(c => c.Rate is not null).Select(c => c.ProductCode)),
                cancellationToken);

            var employeeCode = currentEmployeeContext.EmployeeCode;
            var now = DateTime.Now;

            await ApplyPriceChangesAsync(customerCode, priceChanges, employeeCode, now, cancellationToken);
            await ApplyRateChangesAsync(customerCode, rateChanges, employeeCode, now, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new CustomerProductPriceConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }
        finally
        {
            // 失敗時に未保存の変更が追跡に残らないようにする（成功時も、次の読込を最新にするため問題ない）。
            dbContext.ChangeTracker.Clear();
        }

        logger.LogInformation(
            "得意先別の単価・掛率を登録しました。CustomerCode={CustomerCode} PriceChanges={PriceCount} RateChanges={RateCount}",
            customerCode, priceChanges.Count, rateChanges.Count);
    }

    private async Task ApplyPriceChangesAsync(
        string customerCode, IReadOnlyList<PriceChange> changes, string employeeCode, DateTime now,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            return;
        }

        var codes = changes.Select(c => c.ProductCode).ToList();
        var existing = await dbContext.CustomerProductPrices
            .Where(p => p.CustomerCode == customerCode && codes.Contains(p.ProductCode))
            .ToDictionaryAsync(p => p.ProductCode, cancellationToken);

        foreach (var change in changes)
        {
            existing.TryGetValue(change.ProductCode, out var current);
            EnsureNotModified(current?.RowVersion, change.RowVersion);

            if (change.UnitPrice is not { } unitPrice)
            {
                if (current is { IsDeleted: false })
                {
                    current.IsDeleted = true;
                    Touch(current, employeeCode, now);
                }

                continue;
            }

            if (current is null)
            {
                dbContext.CustomerProductPrices.Add(new CustomerProductPrice
                {
                    CustomerCode = customerCode,
                    ProductCode = change.ProductCode,
                    UnitPrice = unitPrice,
                    CreatedBy = employeeCode,
                    CreatedAt = now,
                    UpdatedBy = employeeCode,
                    UpdatedAt = now,
                });
                continue;
            }

            current.UnitPrice = unitPrice;
            current.IsDeleted = false;
            Touch(current, employeeCode, now);
        }
    }

    private async Task ApplyRateChangesAsync(
        string customerCode, IReadOnlyList<RateChange> changes, string employeeCode, DateTime now,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            return;
        }

        var codes = changes.Select(c => c.ProductCode).ToList();
        var existing = await dbContext.CustomerProductRates
            .Where(r => r.CustomerCode == customerCode && codes.Contains(r.ProductCode))
            .ToDictionaryAsync(r => r.ProductCode, cancellationToken);

        foreach (var change in changes)
        {
            existing.TryGetValue(change.ProductCode, out var current);
            EnsureNotModified(current?.RowVersion, change.RowVersion);

            if (change.Rate is not { } rate)
            {
                if (current is { IsDeleted: false })
                {
                    current.IsDeleted = true;
                    Touch(current, employeeCode, now);
                }

                continue;
            }

            if (current is null)
            {
                dbContext.CustomerProductRates.Add(new CustomerProductRate
                {
                    CustomerCode = customerCode,
                    ProductCode = change.ProductCode,
                    CatalogPriceRate = rate,
                    CreatedBy = employeeCode,
                    CreatedAt = now,
                    UpdatedBy = employeeCode,
                    UpdatedAt = now,
                });
                continue;
            }

            current.CatalogPriceRate = rate;
            current.IsDeleted = false;
            Touch(current, employeeCode, now);
        }
    }

    /// <summary>読込時の RowVersion（行が無ければ <c>null</c>）と現在の行が違えば、他のユーザーが更新している。</summary>
    private static void EnsureNotModified(byte[]? currentRowVersion, byte[]? loadedRowVersion)
    {
        var same = currentRowVersion is null
            ? loadedRowVersion is null
            : loadedRowVersion is not null && currentRowVersion.SequenceEqual(loadedRowVersion);
        if (!same)
        {
            throw new CustomerProductPriceConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }
    }

    private static void Touch(AuditableEntity entity, string employeeCode, DateTime now)
    {
        entity.UpdatedBy = employeeCode;
        entity.UpdatedAt = now;
    }

    private async Task EnsureActiveCustomerAndProductsAsync(
        string customerCode, IEnumerable<string> productCodes, CancellationToken cancellationToken)
    {
        var customerActive = await dbContext.Customers
            .AsNoTracking()
            .AnyAsync(c => c.CustomerCode == customerCode && !c.IsDeleted, cancellationToken);
        if (!customerActive)
        {
            throw new CustomerProductPriceValidationException(
                $"得意先コード「{customerCode}」は登録されていないか、無効です。");
        }

        var codes = productCodes.Distinct().ToList();
        if (codes.Count == 0)
        {
            return;
        }

        var activeCodes = await dbContext.Products
            .AsNoTracking()
            .Where(p => codes.Contains(p.ProductCode) && !p.IsDeleted)
            .Select(p => p.ProductCode)
            .ToListAsync(cancellationToken);
        var missing = codes.Except(activeCodes).FirstOrDefault();
        if (missing is not null)
        {
            throw new CustomerProductPriceValidationException(
                $"商品コード「{missing}」は登録されていないか、無効です。");
        }
    }
}

/// <summary>一覧の1行分。単価・掛率は有効な登録がなければ <c>null</c>。RowVersion は無効化済みの行でも持つ。</summary>
public sealed record CustomerProductSetting(
    string ProductCode,
    string ProductName,
    decimal CatalogPrice,
    decimal? UnitPrice,
    byte[]? PriceRowVersion,
    int? Rate,
    byte[]? RateRowVersion);

/// <summary>単価の変更。<paramref name="UnitPrice"/> が <c>null</c> なら無効化。<paramref name="RowVersion"/> は読込時の値。</summary>
public sealed record PriceChange(string ProductCode, decimal? UnitPrice, byte[]? RowVersion);

/// <summary>定価掛率の変更。<paramref name="Rate"/> が <c>null</c> なら無効化。<paramref name="RowVersion"/> は読込時の値。</summary>
public sealed record RateChange(string ProductCode, int? Rate, byte[]? RowVersion);

/// <summary>得意先／商品／単価・掛率マスタの業務ルール違反（形式チェック済みの入力に対する業務的な拒否）。</summary>
public sealed class CustomerProductPriceValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class CustomerProductPriceConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
