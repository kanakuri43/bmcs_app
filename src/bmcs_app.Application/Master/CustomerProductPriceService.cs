using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>得意先／商品／単価マスタのユースケース。キーは（得意先コード, 商品コード）。</summary>
public class CustomerProductPriceService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<CustomerProductPriceService> logger)
{
    /// <summary>decimal(15,4) に収まる最大値。</summary>
    private const decimal MaxUnitPrice = 99_999_999_999.9999m;

    /// <summary>無効化済みの行も返す（画面が「無効化されています」と案内するため）。</summary>
    public Task<CustomerProductPrice?> GetAsync(
        string customerCode, string productCode, CancellationToken cancellationToken = default)
        => dbContext.CustomerProductPrices
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.CustomerCode == customerCode && p.ProductCode == productCode, cancellationToken);

    /// <summary>登録する。同じ組み合わせが無効化済みなら、単価を更新して有効に戻す。</summary>
    public async Task<CustomerProductPrice> CreateAsync(
        CustomerProductPrice price, CancellationToken cancellationToken = default)
    {
        ValidateUnitPrice(price.UnitPrice);
        await EnsureActiveCustomerAndProductAsync(price.CustomerCode, price.ProductCode, cancellationToken);

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        var existing = await dbContext.CustomerProductPrices
            .SingleOrDefaultAsync(
                p => p.CustomerCode == price.CustomerCode && p.ProductCode == price.ProductCode, cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsDeleted)
            {
                throw new CustomerProductPriceValidationException(
                    $"得意先「{price.CustomerCode}」・商品「{price.ProductCode}」の単価は既に登録されています。");
            }

            existing.UnitPrice = price.UnitPrice;
            existing.IsDeleted = false;
            existing.UpdatedBy = employeeCode;
            existing.UpdatedAt = now;
            await SaveAsync(cancellationToken);

            logger.LogInformation(
                "得意先別単価を有効に戻しました。CustomerCode={CustomerCode} ProductCode={ProductCode}",
                price.CustomerCode, price.ProductCode);
            return existing;
        }

        price.CreatedBy = employeeCode;
        price.CreatedAt = now;
        price.UpdatedBy = employeeCode;
        price.UpdatedAt = now;
        price.IsDeleted = false;

        dbContext.CustomerProductPrices.Add(price);
        await SaveAsync(cancellationToken);

        logger.LogInformation(
            "得意先別単価を登録しました。CustomerCode={CustomerCode} ProductCode={ProductCode}",
            price.CustomerCode, price.ProductCode);
        return price;
    }

    public async Task<CustomerProductPrice> UpdateAsync(
        CustomerProductPrice price, CancellationToken cancellationToken = default)
    {
        ValidateUnitPrice(price.UnitPrice);
        var current = await LoadForUpdateAsync(price, cancellationToken);

        current.UnitPrice = price.UnitPrice;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        await SaveAsync(cancellationToken);

        logger.LogInformation(
            "得意先別単価を更新しました。CustomerCode={CustomerCode} ProductCode={ProductCode}",
            price.CustomerCode, price.ProductCode);
        return current;
    }

    public async Task DeactivateAsync(CustomerProductPrice price, CancellationToken cancellationToken = default)
    {
        var current = await LoadForUpdateAsync(price, cancellationToken);

        current.IsDeleted = true;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        await SaveAsync(cancellationToken);

        logger.LogInformation(
            "得意先別単価を無効化しました。CustomerCode={CustomerCode} ProductCode={ProductCode}",
            price.CustomerCode, price.ProductCode);
    }

    private static void ValidateUnitPrice(decimal unitPrice)
    {
        // 返品はマイナス数量で表すため、単価自体は 0 以上とする。
        if (unitPrice < 0m || unitPrice > MaxUnitPrice)
        {
            throw new CustomerProductPriceValidationException(
                $"単価は 0 以上 {MaxUnitPrice:N4} 以下で入力してください。");
        }
    }

    private async Task<CustomerProductPrice> LoadForUpdateAsync(
        CustomerProductPrice price, CancellationToken cancellationToken)
    {
        var current = await dbContext.CustomerProductPrices
            .SingleOrDefaultAsync(
                p => p.CustomerCode == price.CustomerCode && p.ProductCode == price.ProductCode, cancellationToken)
            ?? throw new CustomerProductPriceValidationException(
                $"得意先「{price.CustomerCode}」・商品「{price.ProductCode}」の単価は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(price.RowVersion!))
        {
            throw new CustomerProductPriceConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
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
            throw new CustomerProductPriceConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
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
            throw new CustomerProductPriceValidationException(
                $"得意先コード「{customerCode}」は登録されていないか、無効です。");
        }

        var productActive = await dbContext.Products
            .AsNoTracking()
            .AnyAsync(p => p.ProductCode == productCode && !p.IsDeleted, cancellationToken);
        if (!productActive)
        {
            throw new CustomerProductPriceValidationException(
                $"商品コード「{productCode}」は登録されていないか、無効です。");
        }
    }
}

/// <summary>得意先／商品／単価マスタの業務ルール違反（形式チェック済みの入力に対する業務的な拒否）。</summary>
public sealed class CustomerProductPriceValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class CustomerProductPriceConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
