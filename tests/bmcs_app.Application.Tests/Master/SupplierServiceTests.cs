using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 仕入先マスタのユースケースの結合テスト。外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// </summary>
public class SupplierServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string Code = "__SUP01";

    [Fact]
    public Task 登録と更新と取得ができる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<SupplierService>();

        var created = await service.CreateAsync(NewSupplier(Code, "初期", null));
        Assert.False(created.IsDeleted);

        await service.UpdateAsync(NewSupplier(Code, "更新後", created.RowVersion));

        var saved = await service.GetActiveByCodeAsync(Code);
        Assert.Equal("更新後", saved!.SupplierName);
        Assert.Contains(await service.GetSuppliersAsync(), s => s.SupplierCode == Code);
    });

    [Fact]
    public Task 同じコードの二重登録は拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<SupplierService>();
        await service.CreateAsync(NewSupplier(Code, "初期", null));

        await Assert.ThrowsAsync<SupplierValidationException>(() => service.CreateAsync(NewSupplier(Code, "重複", null)));
    });

    [Fact]
    public Task 無効化した仕入先は一覧と有効取得から外れ_コード指定の取得には残る() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<SupplierService>();
        var created = await service.CreateAsync(NewSupplier(Code, "初期", null));

        await service.DeactivateAsync(NewSupplier(Code, "初期", created.RowVersion));

        Assert.Null(await service.GetActiveByCodeAsync(Code));
        Assert.DoesNotContain(await service.GetSuppliersAsync(), s => s.SupplierCode == Code);
        Assert.True((await service.GetByCodeAsync(Code))!.IsDeleted);
    });

    [Fact]
    public Task 有効な商品の主仕入先に指定されている間は無効化できない() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<SupplierService>();
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        var created = await service.CreateAsync(NewSupplier(Code, "初期", null));

        dbContext.Products.Add(new Product
        {
            ProductCode = "__SUPPRD",
            ProductName = "主仕入先確認用",
            StandardUnitPriceExclTax = 100,
            StandardUnitPriceInclTax = 110,
            StandardCostPrice = 50,
            TaxCategory = TaxCategory.Standard,
            MainSupplierCode = Code,
            CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        });
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<SupplierValidationException>(
            () => service.DeactivateAsync(NewSupplier(Code, "初期", created.RowVersion)));

        // 商品側の指定を外せば無効化できる。
        var product = await dbContext.Products.SingleAsync(p => p.ProductCode == "__SUPPRD");
        product.MainSupplierCode = null;
        await dbContext.SaveChangesAsync();

        await service.DeactivateAsync(NewSupplier(Code, "初期", created.RowVersion));
        Assert.Null(await service.GetActiveByCodeAsync(Code));
    });

    [Fact]
    public Task 古いRowVersionでの更新と無効化は競合になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<SupplierService>();
        var created = await service.CreateAsync(NewSupplier(Code, "初期", null));
        var stale = (byte[])created.RowVersion!.Clone();

        await service.UpdateAsync(NewSupplier(Code, "先勝ち", stale));

        await Assert.ThrowsAsync<SupplierConcurrencyException>(() => service.UpdateAsync(NewSupplier(Code, "後負け", stale)));
        await Assert.ThrowsAsync<SupplierConcurrencyException>(() => service.DeactivateAsync(NewSupplier(Code, "後負け", stale)));
        Assert.Equal("先勝ち", (await service.GetByCodeAsync(Code))!.SupplierName);
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

    private static Supplier NewSupplier(string code, string name, byte[]? rowVersion) => new()
    {
        SupplierCode = code,
        SupplierName = name,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        RowVersion = rowVersion,
    };
}
