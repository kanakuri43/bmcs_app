using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>得意先／商品／定価掛率マスタのユースケース。キーは（得意先コード, 商品コード）。</summary>
public class CustomerProductRateService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<CustomerProductRateService> logger)
{
    private const int MinRate = 0;

    private const int MaxRate = 100;

    /// <summary>無効化済みの行も返す（画面が「無効化されています」と案内するため）。</summary>
    public Task<CustomerProductRate?> GetAsync(
        string customerCode, string productCode, CancellationToken cancellationToken = default)
        => dbContext.CustomerProductRates
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.CustomerCode == customerCode && p.ProductCode == productCode, cancellationToken);

    /// <summary>登録する。同じ組み合わせが無効化済みなら、定価掛率を更新して有効に戻す。</summary>
    public async Task<CustomerProductRate> CreateAsync(
        CustomerProductRate rate, CancellationToken cancellationToken = default)
    {
        ValidateRate(rate.CatalogPriceRate);
        await EnsureActiveCustomerAndProductAsync(rate.CustomerCode, rate.ProductCode, cancellationToken);

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        var existing = await dbContext.CustomerProductRates
            .SingleOrDefaultAsync(
                p => p.CustomerCode == rate.CustomerCode && p.ProductCode == rate.ProductCode, cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsDeleted)
            {
                throw new CustomerProductRateValidationException(
                    $"得意先「{rate.CustomerCode}」・商品「{rate.ProductCode}」の定価掛率は既に登録されています。");
            }

            existing.CatalogPriceRate = rate.CatalogPriceRate;
            existing.IsDeleted = false;
            existing.UpdatedBy = employeeCode;
            existing.UpdatedAt = now;
            await SaveAsync(cancellationToken);

            logger.LogInformation(
                "得意先別定価掛率を有効に戻しました。CustomerCode={CustomerCode} ProductCode={ProductCode}",
                rate.CustomerCode, rate.ProductCode);
            return existing;
        }

        rate.CreatedBy = employeeCode;
        rate.CreatedAt = now;
        rate.UpdatedBy = employeeCode;
        rate.UpdatedAt = now;
        rate.IsDeleted = false;

        dbContext.CustomerProductRates.Add(rate);
        await SaveAsync(cancellationToken);

        logger.LogInformation(
            "得意先別定価掛率を登録しました。CustomerCode={CustomerCode} ProductCode={ProductCode}",
            rate.CustomerCode, rate.ProductCode);
        return rate;
    }

    public async Task<CustomerProductRate> UpdateAsync(
        CustomerProductRate rate, CancellationToken cancellationToken = default)
    {
        ValidateRate(rate.CatalogPriceRate);
        var current = await LoadForUpdateAsync(rate, cancellationToken);

        current.CatalogPriceRate = rate.CatalogPriceRate;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        await SaveAsync(cancellationToken);

        logger.LogInformation(
            "得意先別定価掛率を更新しました。CustomerCode={CustomerCode} ProductCode={ProductCode}",
            rate.CustomerCode, rate.ProductCode);
        return current;
    }

    public async Task DeactivateAsync(CustomerProductRate rate, CancellationToken cancellationToken = default)
    {
        var current = await LoadForUpdateAsync(rate, cancellationToken);

        current.IsDeleted = true;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        await SaveAsync(cancellationToken);

        logger.LogInformation(
            "得意先別定価掛率を無効化しました。CustomerCode={CustomerCode} ProductCode={ProductCode}",
            rate.CustomerCode, rate.ProductCode);
    }

    private static void ValidateRate(int rate)
    {
        // 定価を超える掛率の運用はない。小数は画面で拒否済み（整数のみ）。
        if (rate < MinRate || rate > MaxRate)
        {
            throw new CustomerProductRateValidationException($"定価掛率は {MinRate} 〜 {MaxRate} の整数で入力してください。");
        }
    }

    private async Task<CustomerProductRate> LoadForUpdateAsync(
        CustomerProductRate rate, CancellationToken cancellationToken)
    {
        var current = await dbContext.CustomerProductRates
            .SingleOrDefaultAsync(
                p => p.CustomerCode == rate.CustomerCode && p.ProductCode == rate.ProductCode, cancellationToken)
            ?? throw new CustomerProductRateValidationException(
                $"得意先「{rate.CustomerCode}」・商品「{rate.ProductCode}」の定価掛率は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(rate.RowVersion!))
        {
            throw new CustomerProductRateConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        return current;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new CustomerProductRateConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }
    }

    private async Task EnsureActiveCustomerAndProductAsync(
        string customerCode, string productCode, CancellationToken cancellationToken)
    {
        var customerActive = await dbContext.Customers
            .AsNoTracking()
            .AnyAsync(c => c.CustomerCode == customerCode && !c.IsDeleted, cancellationToken);
        if (!customerActive)
        {
            throw new CustomerProductRateValidationException(
                $"得意先コード「{customerCode}」は登録されていないか、無効です。");
        }

        var productActive = await dbContext.Products
            .AsNoTracking()
            .AnyAsync(p => p.ProductCode == productCode && !p.IsDeleted, cancellationToken);
        if (!productActive)
        {
            throw new CustomerProductRateValidationException(
                $"商品コード「{productCode}」は登録されていないか、無効です。");
        }
    }
}

/// <summary>得意先／商品／定価掛率マスタの業務ルール違反（形式チェック済みの入力に対する業務的な拒否）。</summary>
public sealed class CustomerProductRateValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class CustomerProductRateConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
