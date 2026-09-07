using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>商品マスタのユースケース（TODO.md 2-2）。</summary>
public class ProductService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<ProductService> logger)
{
    public Task<List<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
        => dbContext.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.ProductCode)
            .ToListAsync(cancellationToken);

    public Task<Product?> GetByCodeAsync(string productCode, CancellationToken cancellationToken = default)
        => dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.ProductCode == productCode, cancellationToken);

    public async Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.Products
            .AsNoTracking()
            .AnyAsync(p => p.ProductCode == product.ProductCode, cancellationToken);
        if (exists)
        {
            throw new ProductValidationException($"商品コード「{product.ProductCode}」は既に登録されています。");
        }

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        product.CreatedBy = employeeCode;
        product.CreatedAt = now;
        product.UpdatedBy = employeeCode;
        product.UpdatedAt = now;
        product.IsDeleted = false;

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("商品を登録しました。ProductCode={ProductCode}", product.ProductCode);
        return product;
    }

    public async Task<Product> UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.Products
            .SingleOrDefaultAsync(p => p.ProductCode == product.ProductCode, cancellationToken)
            ?? throw new ProductValidationException($"商品コード「{product.ProductCode}」は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(product.RowVersion!))
        {
            throw new ProductConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        current.ProductName = product.ProductName;
        current.ProductNameKana = product.ProductNameKana;
        current.Specification = product.Specification;
        current.UnitName = product.UnitName;
        current.StandardUnitPriceExclTax = product.StandardUnitPriceExclTax;
        current.StandardUnitPriceInclTax = product.StandardUnitPriceInclTax;
        current.StandardCostPrice = product.StandardCostPrice;
        current.TaxCategory = product.TaxCategory;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ProductConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("商品を更新しました。ProductCode={ProductCode}", product.ProductCode);
        return current;
    }

    public async Task DeactivateAsync(Product product, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.Products
            .SingleOrDefaultAsync(p => p.ProductCode == product.ProductCode, cancellationToken)
            ?? throw new ProductValidationException($"商品コード「{product.ProductCode}」は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(product.RowVersion!))
        {
            throw new ProductConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        current.IsDeleted = true;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ProductConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("商品を無効化しました。ProductCode={ProductCode}", product.ProductCode);
    }
}

/// <summary>商品マスタの業務ルール違反（形式チェック済みの入力に対する業務的な拒否）。</summary>
public sealed class ProductValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class ProductConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
