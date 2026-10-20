using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 単価の初期値の解決（単価マスタ → 定価掛率マスタ → 商品マスタ）の結合テスト。
/// 外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// </summary>
public class UnitPriceServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    // CUS001＝請求単位・切捨、CUS003＝内税明細単位・切上（DevDatabaseFixture の参照得意先）。
    private const string InvoiceCustomer = "CUS001";
    private const string LineCustomer = "CUS003";

    // 税率マスタは 2019-10-01 以降（通常10%・軽減8%）が適用される日付を使う。
    private static readonly DateOnly SlipDate = new(2026, 10, 20);

    [Fact]
    public Task 単価マスタと掛率マスタと商品マスタが優先順に使われる() => RunAsync(async (dbContext, service) =>
    {
        var fixedPrice = NewProduct("__UP01", exclTax: 1000m, catalogPrice: 2000m);
        var ratedOnly = NewProduct("__UP02", exclTax: 1000m, catalogPrice: 2000m);
        var masterOnly = NewProduct("__UP03", exclTax: 1000m, catalogPrice: 2000m);
        dbContext.Products.AddRange(fixedPrice, ratedOnly, masterOnly);
        dbContext.CustomerProductPrices.Add(NewPrice(InvoiceCustomer, "__UP01", 777m));
        dbContext.CustomerProductRates.Add(NewRate(InvoiceCustomer, "__UP01", 50));
        dbContext.CustomerProductRates.Add(NewRate(InvoiceCustomer, "__UP02", 80));
        await dbContext.SaveChangesAsync();

        var resolver = await service.CreateResolverAsync(await GetCustomerAsync(dbContext, InvoiceCustomer), SlipDate);

        Assert.Equal(777m, resolver.Resolve(fixedPrice));    // ① 単価マスタ（掛率マスタより優先）
        Assert.Equal(1600m, resolver.Resolve(ratedOnly));    // ② 2000 × 80%
        Assert.Equal(1000m, resolver.Resolve(masterOnly));   // ③ 商品マスタの外税単価
    });

    [Fact]
    public Task 論理削除したマスタ行は無視される() => RunAsync(async (dbContext, service) =>
    {
        var product = NewProduct("__UP04", exclTax: 1000m, catalogPrice: 2000m);
        dbContext.Products.Add(product);
        var price = NewPrice(InvoiceCustomer, "__UP04", 777m);
        price.IsDeleted = true;
        var rate = NewRate(InvoiceCustomer, "__UP04", 80);
        dbContext.CustomerProductPrices.Add(price);
        dbContext.CustomerProductRates.Add(rate);
        await dbContext.SaveChangesAsync();

        var customer = await GetCustomerAsync(dbContext, InvoiceCustomer);
        Assert.Equal(1600m, (await service.CreateResolverAsync(customer, SlipDate)).Resolve(product));

        rate.IsDeleted = true;
        await dbContext.SaveChangesAsync();
        Assert.Equal(1000m, (await service.CreateResolverAsync(customer, SlipDate)).Resolve(product));
    });

    [Fact]
    public Task 他の得意先のマスタ行は使われない() => RunAsync(async (dbContext, service) =>
    {
        var product = NewProduct("__UP05", exclTax: 1000m, catalogPrice: 2000m);
        dbContext.Products.Add(product);
        dbContext.CustomerProductPrices.Add(NewPrice(LineCustomer, "__UP05", 777m));
        await dbContext.SaveChangesAsync();

        var resolver = await service.CreateResolverAsync(await GetCustomerAsync(dbContext, InvoiceCustomer), SlipDate);

        Assert.Equal(1000m, resolver.Resolve(product));
    });

    [Fact]
    public Task 内税得意先の掛率単価は税率マスタで税込換算し得意先の端数区分で丸める() => RunAsync(async (dbContext, service) =>
    {
        var standard = NewProduct("__UP06", exclTax: 0m, catalogPrice: 1000m, TaxCategory.Standard);
        var reduced = NewProduct("__UP07", exclTax: 0m, catalogPrice: 999m, TaxCategory.Reduced);
        var exempt = NewProduct("__UP08", exclTax: 0m, catalogPrice: 1000m, TaxCategory.TaxExempt);
        dbContext.Products.AddRange(standard, reduced, exempt);
        dbContext.CustomerProductRates.Add(NewRate(LineCustomer, "__UP06", 80));
        dbContext.CustomerProductRates.Add(NewRate(LineCustomer, "__UP07", 83));
        dbContext.CustomerProductRates.Add(NewRate(LineCustomer, "__UP08", 80));
        await dbContext.SaveChangesAsync();

        var resolver = await service.CreateResolverAsync(await GetCustomerAsync(dbContext, LineCustomer), SlipDate);

        Assert.Equal(880m, resolver.Resolve(standard));  // 1000 × 80% × 1.10
        Assert.Equal(896m, resolver.Resolve(reduced));   // 999 × 83% × 1.08 = 895.5036 を切上
        Assert.Equal(800m, resolver.Resolve(exempt));    // 非課税は換算しない
    });

    [Fact]
    public Task 得意先未選択は商品マスタの外税単価() => RunAsync(async (dbContext, service) =>
    {
        var product = NewProduct("__UP09", exclTax: 1000m, catalogPrice: 2000m);
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync();

        var resolver = await service.CreateResolverAsync(null, SlipDate);

        Assert.Equal(1000m, resolver.Resolve(product));
    });

    private async Task RunAsync(Func<BmcsDbContext, UnitPriceService, Task> body)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await body(dbContext, scope.ServiceProvider.GetRequiredService<UnitPriceService>());
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static Task<Customer> GetCustomerAsync(BmcsDbContext dbContext, string code)
        => dbContext.Customers.AsNoTracking().SingleAsync(c => c.CustomerCode == code);

    private static Product NewProduct(
        string code, decimal exclTax, decimal catalogPrice, TaxCategory taxCategory = TaxCategory.Standard) => new()
    {
        ProductCode = code,
        ProductName = "テスト商品",
        StandardUnitPriceExclTax = exclTax,
        StandardUnitPriceInclTax = exclTax,
        StandardCostPrice = 0m,
        CatalogPrice = catalogPrice,
        TaxCategory = taxCategory,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
    };

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
