using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>商品マスタのユースケース。</summary>
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

        await ValidateMainSupplierAsync(product.MainSupplierCode, cancellationToken);

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

        if (current.MainSupplierCode != product.MainSupplierCode)
        {
            // 変更した場合のみ検証する。既存の指定をそのまま保存し直すだけなら、
            // 指定先が後から無効化されていても拒否しない（仕入先の無効化は主仕入先参照中は拒否されるため通常起きない）。
            await ValidateMainSupplierAsync(product.MainSupplierCode, cancellationToken);
            current.MainSupplierCode = product.MainSupplierCode;
        }

        current.ProductName = product.ProductName;
        current.ProductNameKana = product.ProductNameKana;
        current.JanCode = product.JanCode;
        current.Specification = product.Specification;
        current.UnitName = product.UnitName;
        current.StandardUnitPriceExclTax = product.StandardUnitPriceExclTax;
        current.StandardUnitPriceInclTax = product.StandardUnitPriceInclTax;
        current.StandardCostPrice = product.StandardCostPrice;
        current.CatalogPrice = product.CatalogPrice;
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

    /// <summary>主仕入先は、指定するなら有効な仕入先でなければならない（空はなし）。</summary>
    private async Task ValidateMainSupplierAsync(string? supplierCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(supplierCode))
        {
            return;
        }

        var isActive = await dbContext.Suppliers
            .AsNoTracking()
            .AnyAsync(s => s.SupplierCode == supplierCode && !s.IsDeleted, cancellationToken);
        if (!isActive)
        {
            throw new ProductValidationException($"主仕入先コード「{supplierCode}」は登録されていないか、無効です。");
        }
    }
}

/// <summary>商品マスタの業務ルール違反（形式チェック済みの入力に対する業務的な拒否）。</summary>
public sealed class ProductValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class ProductConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
