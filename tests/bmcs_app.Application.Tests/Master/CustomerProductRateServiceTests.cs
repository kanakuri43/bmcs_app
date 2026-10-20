using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 得意先／商品／定価掛率マスタの結合テスト。外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// </summary>
public class CustomerProductRateServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string ReferenceCustomer = "CUS001";
    private const string ReferenceProduct = "PRD001";

    [Fact]
    public Task 登録_更新_無効化ができる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductRateService>();

        var created = await service.CreateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 80, null));
        Assert.Equal(80, (await service.GetAsync(ReferenceCustomer, ReferenceProduct))!.CatalogPriceRate);

        var updated = await service.UpdateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 75, created.RowVersion));
        Assert.Equal(75, updated.CatalogPriceRate);

        var current = (await service.GetAsync(ReferenceCustomer, ReferenceProduct))!;
        await service.DeactivateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 75, current.RowVersion));

        Assert.True((await service.GetAsync(ReferenceCustomer, ReferenceProduct))!.IsDeleted);
    });

    [Fact]
    public Task 無効化した組み合わせは再登録で有効に戻り掛率が新しい値になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductRateService>();
        var created = await service.CreateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 80, null));
        await service.DeactivateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 80, created.RowVersion));

        await service.CreateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 60, null));

        var current = (await service.GetAsync(ReferenceCustomer, ReferenceProduct))!;
        Assert.False(current.IsDeleted);
        Assert.Equal(60, current.CatalogPriceRate);
    });

    [Fact]
    public Task 有効な組み合わせの二重登録は拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductRateService>();
        await service.CreateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 80, null));

        await Assert.ThrowsAsync<CustomerProductRateValidationException>(
            () => service.CreateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 90, null)));
    });

    [Fact]
    public Task 存在しない得意先と商品は拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductRateService>();

        await Assert.ThrowsAsync<CustomerProductRateValidationException>(
            () => service.CreateAsync(NewRate("__NOCUS", ReferenceProduct, 80, null)));
        await Assert.ThrowsAsync<CustomerProductRateValidationException>(
            () => service.CreateAsync(NewRate(ReferenceCustomer, "__NOPRD", 80, null)));
        Assert.Null(await service.GetAsync("__NOCUS", ReferenceProduct));
    });

    [Fact]
    public Task 無効な得意先と商品は拒否される() => RunAsync(async sp =>
    {
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        var customerService = sp.GetRequiredService<CustomerService>();
        var customer = await customerService.CreateAsync(NewCustomer("__TCR90"));
        await customerService.DeactivateAsync(customer);

        var productService = sp.GetRequiredService<ProductService>();
        var product = await productService.CreateAsync(NewProduct("__TCR91"));
        await productService.DeactivateAsync(product);

        var service = sp.GetRequiredService<CustomerProductRateService>();
        await Assert.ThrowsAsync<CustomerProductRateValidationException>(
            () => service.CreateAsync(NewRate("__TCR90", ReferenceProduct, 80, null)));
        await Assert.ThrowsAsync<CustomerProductRateValidationException>(
            () => service.CreateAsync(NewRate(ReferenceCustomer, "__TCR91", 80, null)));
        Assert.Empty(await dbContext.CustomerProductRates.Where(r => r.CustomerCode == "__TCR90").ToListAsync());
    });

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public Task 範囲外の掛率は登録も更新も拒否される(int rate) => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductRateService>();
        await Assert.ThrowsAsync<CustomerProductRateValidationException>(
            () => service.CreateAsync(NewRate(ReferenceCustomer, ReferenceProduct, rate, null)));

        var created = await service.CreateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 80, null));
        await Assert.ThrowsAsync<CustomerProductRateValidationException>(
            () => service.UpdateAsync(NewRate(ReferenceCustomer, ReferenceProduct, rate, created.RowVersion)));
    });

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public Task 掛率0と100は登録できる(int rate) => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductRateService>();

        await service.CreateAsync(NewRate(ReferenceCustomer, ReferenceProduct, rate, null));

        Assert.Equal(rate, (await service.GetAsync(ReferenceCustomer, ReferenceProduct))!.CatalogPriceRate);
    });

    [Fact]
    public Task 古いRowVersionでの更新と無効化は競合として拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductRateService>();
        var created = await service.CreateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 80, null));
        var stale = (byte[])created.RowVersion!.Clone();

        await service.UpdateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 70, stale));

        await Assert.ThrowsAsync<CustomerProductRateConcurrencyException>(
            () => service.UpdateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 60, stale)));
        await Assert.ThrowsAsync<CustomerProductRateConcurrencyException>(
            () => service.DeactivateAsync(NewRate(ReferenceCustomer, ReferenceProduct, 60, stale)));
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

    private static CustomerProductRate NewRate(string customerCode, string productCode, int rate, byte[]? rowVersion) => new()
    {
        CustomerCode = customerCode,
        ProductCode = productCode,
        CatalogPriceRate = rate,
        RowVersion = rowVersion,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
    };

    private static Customer NewCustomer(string code) => new()
    {
        CustomerCode = code,
        CustomerName = "テスト得意先",
        ClosingDay = 15,
        TaxUnit = TaxUnit.Invoice,
        RoundingType = RoundingType.Floor,
        PrintRepresentativeFlag = false,
        BillingCustomerCode = code,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
    };

    private static Product NewProduct(string code) => new()
    {
        ProductCode = code,
        ProductName = "テスト商品",
        StandardUnitPriceExclTax = 100m,
        StandardUnitPriceInclTax = 110m,
        StandardCostPrice = 50m,
        TaxCategory = TaxCategory.Standard,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
    };
}
