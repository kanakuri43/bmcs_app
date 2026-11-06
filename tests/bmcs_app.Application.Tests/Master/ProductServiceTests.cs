using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 商品マスタの主仕入先（main_supplier_code）の結合テスト。外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// </summary>
public class ProductServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string ProductCode = "__PRD01";
    private const string SupplierCode = "__SUP01";

    [Fact]
    public Task 主仕入先を指定して登録でき_変更と解除もできる() => RunAsync(async sp =>
    {
        var products = sp.GetRequiredService<ProductService>();
        await sp.GetRequiredService<SupplierService>().CreateAsync(NewSupplier(SupplierCode));
        await sp.GetRequiredService<SupplierService>().CreateAsync(NewSupplier("__SUP02"));

        var created = await products.CreateAsync(NewProduct(SupplierCode, null));
        Assert.Equal(SupplierCode, (await products.GetByCodeAsync(ProductCode))!.MainSupplierCode);

        var updated = await products.UpdateAsync(NewProduct("__SUP02", created.RowVersion));
        Assert.Equal("__SUP02", (await products.GetByCodeAsync(ProductCode))!.MainSupplierCode);

        await products.UpdateAsync(NewProduct(null, updated.RowVersion));
        Assert.Null((await products.GetByCodeAsync(ProductCode))!.MainSupplierCode);
    });

    [Fact]
    public Task 未登録または無効な仕入先は主仕入先に指定できない() => RunAsync(async sp =>
    {
        var products = sp.GetRequiredService<ProductService>();
        var suppliers = sp.GetRequiredService<SupplierService>();

        await Assert.ThrowsAsync<ProductValidationException>(() => products.CreateAsync(NewProduct("__NOSUP", null)));

        var supplier = await suppliers.CreateAsync(NewSupplier(SupplierCode));
        await suppliers.DeactivateAsync(NewSupplier(SupplierCode, supplier.RowVersion));
        await Assert.ThrowsAsync<ProductValidationException>(() => products.CreateAsync(NewProduct(SupplierCode, null)));

        var created = await products.CreateAsync(NewProduct(null, null));
        await Assert.ThrowsAsync<ProductValidationException>(
            () => products.UpdateAsync(NewProduct(SupplierCode, created.RowVersion)));
    });

    [Fact]
    public Task 主仕入先を変えない再保存は検証しない() => RunAsync(async sp =>
    {
        var products = sp.GetRequiredService<ProductService>();
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        await sp.GetRequiredService<SupplierService>().CreateAsync(NewSupplier(SupplierCode));
        var created = await products.CreateAsync(NewProduct(SupplierCode, null));

        // 仕入先を直接無効化済みにしても（通常はサービスが拒否する状態を作る）、主仕入先を変えない保存は通る。
        dbContext.Suppliers.Single(s => s.SupplierCode == SupplierCode).IsDeleted = true;
        await dbContext.SaveChangesAsync();

        var product = NewProduct(SupplierCode, created.RowVersion);
        product.ProductName = "名称だけ変更";
        await products.UpdateAsync(product);
        Assert.Equal("名称だけ変更", (await products.GetByCodeAsync(ProductCode))!.ProductName);
    });

    private async Task RunAsync(Func<IServiceProvider, Task> body)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await body(scope.ServiceProvider);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static Product NewProduct(string? mainSupplierCode, byte[]? rowVersion) => new()
    {
        ProductCode = ProductCode,
        ProductName = "主仕入先確認用",
        StandardUnitPriceExclTax = 100,
        StandardUnitPriceInclTax = 110,
        StandardCostPrice = 50,
        TaxCategory = TaxCategory.Standard,
        MainSupplierCode = mainSupplierCode,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        RowVersion = rowVersion,
    };

    private static Supplier NewSupplier(string code, byte[]? rowVersion = null) => new()
    {
        SupplierCode = code,
        SupplierName = "テスト仕入先",
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        RowVersion = rowVersion,
    };
}
