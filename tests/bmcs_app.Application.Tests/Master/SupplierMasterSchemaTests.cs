using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 仕入先マスタ・仕入先別原価・商品の主仕入先のスキーマ（主キー・FK・CHECK）の結合テスト。
/// 外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// </summary>
public class SupplierMasterSchemaTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string ReferenceProduct = "PRD001";
    private const string TestSupplier = "__SUP01";

    [Fact]
    public Task 仕入先は書き込みと読み取りができ主キーで守られる() => RunAsync(async dbContext =>
    {
        dbContext.Suppliers.Add(NewSupplier(TestSupplier));
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var saved = await dbContext.Suppliers.SingleAsync(s => s.SupplierCode == TestSupplier);
        Assert.Equal("テスト仕入先", saved.SupplierName);
        Assert.False(saved.IsDeleted);

        dbContext.ChangeTracker.Clear();
        dbContext.Suppliers.Add(NewSupplier(TestSupplier));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    });

    [Fact]
    public Task 仕入先別原価は書き込みと読み取りができ主キーとFKで守られる() => RunAsync(async dbContext =>
    {
        dbContext.Suppliers.Add(NewSupplier(TestSupplier));
        await dbContext.SaveChangesAsync();

        dbContext.ProductSupplierCosts.Add(NewCost(ReferenceProduct, TestSupplier, 123.4567m));
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var saved = await dbContext.ProductSupplierCosts.SingleAsync(
            c => c.ProductCode == ReferenceProduct && c.SupplierCode == TestSupplier);
        Assert.Equal(123.4567m, saved.CostPrice);

        // 同一の商品・仕入先の2件目は主キー違反。
        dbContext.ChangeTracker.Clear();
        dbContext.ProductSupplierCosts.Add(NewCost(ReferenceProduct, TestSupplier, 1m));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        // 未登録の商品・仕入先はFK違反。
        dbContext.ChangeTracker.Clear();
        dbContext.ProductSupplierCosts.Add(NewCost("__NOPRD", TestSupplier, 1m));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        dbContext.ChangeTracker.Clear();
        dbContext.ProductSupplierCosts.Add(NewCost(ReferenceProduct, "__NOSUP", 1m));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    });

    [Fact]
    public Task 仕入先別原価は負の値を登録できない() => RunAsync(async dbContext =>
    {
        dbContext.Suppliers.Add(NewSupplier(TestSupplier));
        await dbContext.SaveChangesAsync();

        dbContext.ProductSupplierCosts.Add(NewCost(ReferenceProduct, TestSupplier, -0.0001m));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    });

    [Fact]
    public Task 商品の主仕入先は未登録の仕入先を指定できない() => RunAsync(async dbContext =>
    {
        var product = await dbContext.Products.SingleAsync(p => p.ProductCode == ReferenceProduct);

        product.MainSupplierCode = "__NOSUP";
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        dbContext.ChangeTracker.Clear();
        dbContext.Suppliers.Add(NewSupplier(TestSupplier));
        await dbContext.SaveChangesAsync();

        product = await dbContext.Products.SingleAsync(p => p.ProductCode == ReferenceProduct);
        product.MainSupplierCode = TestSupplier;
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var saved = await dbContext.Products.AsNoTracking().SingleAsync(p => p.ProductCode == ReferenceProduct);
        Assert.Equal(TestSupplier, saved.MainSupplierCode);
    });

    private async Task RunAsync(Func<BmcsDbContext, Task> body)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await body(dbContext);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static Supplier NewSupplier(string code) => new()
    {
        SupplierCode = code,
        SupplierName = "テスト仕入先",
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
    };

    private static ProductSupplierCost NewCost(string productCode, string supplierCode, decimal cost) => new()
    {
        ProductCode = productCode,
        SupplierCode = supplierCode,
        CostPrice = cost,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
    };
}
