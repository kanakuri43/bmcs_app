using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 得意先／商品／単価マスタの結合テスト。外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// </summary>
public class CustomerProductPriceServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string ReferenceCustomer = "CUS001";
    private const string ReferenceProduct = "PRD001";

    [Fact]
    public Task 登録_更新_無効化ができる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();

        var created = await service.CreateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 123.4567m, null));
        Assert.Equal(123.4567m, (await service.GetAsync(ReferenceCustomer, ReferenceProduct))!.UnitPrice);

        var updated = await service.UpdateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 200m, created.RowVersion));
        Assert.Equal(200m, updated.UnitPrice);

        var current = (await service.GetAsync(ReferenceCustomer, ReferenceProduct))!;
        await service.DeactivateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 200m, current.RowVersion));

        Assert.True((await service.GetAsync(ReferenceCustomer, ReferenceProduct))!.IsDeleted);
    });

    [Fact]
    public Task 無効化した組み合わせは再登録で有効に戻り単価が新しい値になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();
        var created = await service.CreateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 100m, null));
        await service.DeactivateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 100m, created.RowVersion));

        await service.CreateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 300m, null));

        var current = (await service.GetAsync(ReferenceCustomer, ReferenceProduct))!;
        Assert.False(current.IsDeleted);
        Assert.Equal(300m, current.UnitPrice);
    });

    [Fact]
    public Task 有効な組み合わせの二重登録は拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();
        await service.CreateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 100m, null));

        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.CreateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 200m, null)));
    });

    [Fact]
    public Task 存在しない得意先と商品は拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();

        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.CreateAsync(NewPrice("__NOCUS", ReferenceProduct, 100m, null)));
        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.CreateAsync(NewPrice(ReferenceCustomer, "__NOPRD", 100m, null)));
        Assert.Null(await service.GetAsync("__NOCUS", ReferenceProduct));
    });

    [Fact]
    public Task 無効な得意先と商品は拒否される() => RunAsync(async sp =>
    {
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        var customerService = sp.GetRequiredService<CustomerService>();
        var customer = await customerService.CreateAsync(NewCustomer("__TCP90"));
        await customerService.DeactivateAsync(customer);

        var productService = sp.GetRequiredService<ProductService>();
        var product = await productService.CreateAsync(NewProduct("__TCP91"));
        await productService.DeactivateAsync(product);

        var service = sp.GetRequiredService<CustomerProductPriceService>();
        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.CreateAsync(NewPrice("__TCP90", ReferenceProduct, 100m, null)));
        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.CreateAsync(NewPrice(ReferenceCustomer, "__TCP91", 100m, null)));
        Assert.Empty(await dbContext.CustomerProductPrices.Where(p => p.CustomerCode == "__TCP90").ToListAsync());
    });

    [Theory]
    [InlineData("-0.0001")]
    [InlineData("100000000000")]
    public Task 範囲外の単価は拒否される(string unitPrice) => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();

        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.CreateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, decimal.Parse(unitPrice), null)));
    });

    [Fact]
    public Task 単価0と上限値は登録できる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();

        await service.CreateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 0m, null));
        var created = (await service.GetAsync(ReferenceCustomer, ReferenceProduct))!;
        var updated = await service.UpdateAsync(
            NewPrice(ReferenceCustomer, ReferenceProduct, 99_999_999_999.9999m, created.RowVersion));

        Assert.Equal(99_999_999_999.9999m, updated.UnitPrice);
    });

    [Fact]
    public Task 古いRowVersionでの更新と無効化は競合として拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();
        var created = await service.CreateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 100m, null));
        var stale = (byte[])created.RowVersion!.Clone();

        await service.UpdateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 200m, stale));

        await Assert.ThrowsAsync<CustomerProductPriceConcurrencyException>(
            () => service.UpdateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 300m, stale)));
        await Assert.ThrowsAsync<CustomerProductPriceConcurrencyException>(
            () => service.DeactivateAsync(NewPrice(ReferenceCustomer, ReferenceProduct, 300m, stale)));
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

    private static CustomerProductPrice NewPrice(string customerCode, string productCode, decimal unitPrice, byte[]? rowVersion) => new()
    {
        CustomerCode = customerCode,
        ProductCode = productCode,
        UnitPrice = unitPrice,
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
