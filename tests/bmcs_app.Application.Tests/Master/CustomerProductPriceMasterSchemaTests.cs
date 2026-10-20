using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 得意先／商品／単価マスタ・定価掛率マスタのスキーマ（主キー・FK・CHECK）の結合テスト。
/// 外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// </summary>
public class CustomerProductPriceMasterSchemaTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string ReferenceCustomer = "CUS001";
    private const string ReferenceProduct = "PRD001";

    [Fact]
    public Task 単価マスタは書き込みと読み取りができ主キーとFKで守られる() => RunAsync(async dbContext =>
    {
        dbContext.CustomerProductPrices.Add(NewPrice(ReferenceCustomer, ReferenceProduct, 123.4567m));
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var saved = await dbContext.CustomerProductPrices.SingleAsync(
            p => p.CustomerCode == ReferenceCustomer && p.ProductCode == ReferenceProduct);
        Assert.Equal(123.4567m, saved.UnitPrice);

        // 同一の得意先・商品の2件目は主キー違反。
        dbContext.ChangeTracker.Clear();
        dbContext.CustomerProductPrices.Add(NewPrice(ReferenceCustomer, ReferenceProduct, 1m));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        // 未登録の得意先・商品はFK違反。
        dbContext.ChangeTracker.Clear();
        dbContext.CustomerProductPrices.Add(NewPrice("__NOCUS", ReferenceProduct, 1m));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        dbContext.ChangeTracker.Clear();
        dbContext.CustomerProductPrices.Add(NewPrice(ReferenceCustomer, "__NOPRD", 1m));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    });

    [Fact]
    public Task 掛率マスタは書き込みと読み取りができ主キーとFKで守られる() => RunAsync(async dbContext =>
    {
        dbContext.CustomerProductRates.Add(NewRate(ReferenceCustomer, ReferenceProduct, 80));
        await dbContext.SaveChangesAsync();

        dbContext.ChangeTracker.Clear();
        var saved = await dbContext.CustomerProductRates.SingleAsync(
            r => r.CustomerCode == ReferenceCustomer && r.ProductCode == ReferenceProduct);
        Assert.Equal(80, saved.CatalogPriceRate);

        dbContext.ChangeTracker.Clear();
        dbContext.CustomerProductRates.Add(NewRate(ReferenceCustomer, ReferenceProduct, 90));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        dbContext.ChangeTracker.Clear();
        dbContext.CustomerProductRates.Add(NewRate("__NOCUS", ReferenceProduct, 90));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        dbContext.ChangeTracker.Clear();
        dbContext.CustomerProductRates.Add(NewRate(ReferenceCustomer, "__NOPRD", 90));
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    });

    [Theory]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(-1, false)]
    [InlineData(101, false)]
    public Task 掛率は0から100の整数だけ登録できる(int rate, bool accepted) => RunAsync(async dbContext =>
    {
        dbContext.CustomerProductRates.Add(NewRate(ReferenceCustomer, ReferenceProduct, rate));
        if (accepted)
        {
            await dbContext.SaveChangesAsync();
        }
        else
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        }
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

    private static CustomerProductPrice NewPrice(string customerCode, string productCode, decimal unitPrice) => new()
    {
        CustomerCode = customerCode,
        ProductCode = productCode,
        UnitPrice = unitPrice,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
    };

    private static CustomerProductRate NewRate(string customerCode, string productCode, int rate) => new()
    {
        CustomerCode = customerCode,
        ProductCode = productCode,
        CatalogPriceRate = rate,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
    };
}
