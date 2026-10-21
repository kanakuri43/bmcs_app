using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 得意先／商品／単価・掛率マスタの結合テスト。外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// </summary>
public class CustomerProductPriceServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string ReferenceCustomer = "CUS001";
    private const string ReferenceProduct = "PRD001";
    private const string OtherProduct = "PRD002";

    [Fact]
    public Task 単価と掛率を新規登録して更新できる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();

        await service.SaveSettingsAsync(
            ReferenceCustomer,
            [new PriceChange(ReferenceProduct, 123.4567m, null)],
            [new RateChange(ReferenceProduct, 80, null)]);
        var created = await GetAsync(service, ReferenceProduct);
        Assert.Equal(123.4567m, created.UnitPrice);
        Assert.Equal(80, created.Rate);

        await service.SaveSettingsAsync(
            ReferenceCustomer,
            [new PriceChange(ReferenceProduct, 200m, created.PriceRowVersion)],
            [new RateChange(ReferenceProduct, 0, created.RateRowVersion)]);
        var updated = await GetAsync(service, ReferenceProduct);
        Assert.Equal(200m, updated.UnitPrice);
        Assert.Equal(0, updated.Rate);
    });

    [Fact]
    public Task 空欄で登録すると無効化され一覧では未登録になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();
        await service.SaveSettingsAsync(
            ReferenceCustomer, [new PriceChange(ReferenceProduct, 100m, null)], [new RateChange(ReferenceProduct, 50, null)]);
        var created = await GetAsync(service, ReferenceProduct);

        await service.SaveSettingsAsync(
            ReferenceCustomer,
            [new PriceChange(ReferenceProduct, null, created.PriceRowVersion)],
            [new RateChange(ReferenceProduct, null, created.RateRowVersion)]);

        var deactivated = await GetAsync(service, ReferenceProduct);
        Assert.Null(deactivated.UnitPrice);
        Assert.Null(deactivated.Rate);
        Assert.NotNull(deactivated.PriceRowVersion);
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        Assert.True((await dbContext.CustomerProductPrices.AsNoTracking()
            .SingleAsync(p => p.CustomerCode == ReferenceCustomer && p.ProductCode == ReferenceProduct)).IsDeleted);
        Assert.True((await dbContext.CustomerProductRates.AsNoTracking()
            .SingleAsync(r => r.CustomerCode == ReferenceCustomer && r.ProductCode == ReferenceProduct)).IsDeleted);
    });

    [Fact]
    public Task 未登録の組み合わせを空欄で登録しても何も作られない() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();

        await service.SaveSettingsAsync(
            ReferenceCustomer, [new PriceChange(ReferenceProduct, null, null)], [new RateChange(ReferenceProduct, null, null)]);

        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        Assert.Empty(await dbContext.CustomerProductPrices.Where(p => p.CustomerCode == ReferenceCustomer).ToListAsync());
        Assert.Empty(await dbContext.CustomerProductRates.Where(r => r.CustomerCode == ReferenceCustomer).ToListAsync());
    });

    [Fact]
    public Task 無効化した組み合わせは再登録で有効に戻り新しい値になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();
        await service.SaveSettingsAsync(
            ReferenceCustomer, [new PriceChange(ReferenceProduct, 100m, null)], [new RateChange(ReferenceProduct, 50, null)]);
        var created = await GetAsync(service, ReferenceProduct);
        await service.SaveSettingsAsync(
            ReferenceCustomer,
            [new PriceChange(ReferenceProduct, null, created.PriceRowVersion)],
            [new RateChange(ReferenceProduct, null, created.RateRowVersion)]);
        var deactivated = await GetAsync(service, ReferenceProduct);

        await service.SaveSettingsAsync(
            ReferenceCustomer,
            [new PriceChange(ReferenceProduct, 300m, deactivated.PriceRowVersion)],
            [new RateChange(ReferenceProduct, 70, deactivated.RateRowVersion)]);

        var revived = await GetAsync(service, ReferenceProduct);
        Assert.Equal(300m, revived.UnitPrice);
        Assert.Equal(70, revived.Rate);
    });

    [Fact]
    public Task 一覧は有効な商品の全件を返し他の得意先の登録は含まない() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();
        await service.SaveSettingsAsync(ReferenceCustomer, [new PriceChange(ReferenceProduct, 100m, null)], []);
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        var activeCount = await dbContext.Products.CountAsync(p => !p.IsDeleted);

        var own = await service.GetSettingsAsync(ReferenceCustomer);
        var other = await service.GetSettingsAsync("CUS002");

        Assert.Equal(activeCount, own.Count);
        Assert.Equal(100m, own.Single(s => s.ProductCode == ReferenceProduct).UnitPrice);
        Assert.Null(own.Single(s => s.ProductCode == OtherProduct).UnitPrice);
        Assert.All(other, s => Assert.Null(s.UnitPrice));
    });

    [Fact]
    public Task 存在しない得意先と商品は拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();

        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.SaveSettingsAsync("__NOCUS", [new PriceChange(ReferenceProduct, 100m, null)], []));
        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.SaveSettingsAsync(ReferenceCustomer, [new PriceChange("__NOPRD", 100m, null)], []));
        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.SaveSettingsAsync(ReferenceCustomer, [], [new RateChange("__NOPRD", 10, null)]));
    });

    [Fact]
    public Task 無効な得意先と商品は拒否される() => RunAsync(async sp =>
    {
        var customerService = sp.GetRequiredService<CustomerService>();
        var customer = await customerService.CreateAsync(NewCustomer("__TCP90"));
        await customerService.DeactivateAsync(customer);

        var productService = sp.GetRequiredService<ProductService>();
        var product = await productService.CreateAsync(NewProduct("__TCP91"));
        await productService.DeactivateAsync(product);

        var service = sp.GetRequiredService<CustomerProductPriceService>();
        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.SaveSettingsAsync("__TCP90", [new PriceChange(ReferenceProduct, 100m, null)], []));
        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.SaveSettingsAsync(ReferenceCustomer, [new PriceChange("__TCP91", 100m, null)], []));
    });

    [Theory]
    [InlineData("-0.0001")]
    [InlineData("100000000000")]
    public Task 範囲外の単価は拒否される(string unitPrice) => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();

        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.SaveSettingsAsync(ReferenceCustomer, [new PriceChange(ReferenceProduct, decimal.Parse(unitPrice), null)], []));
    });

    [Fact]
    public Task 単価0と上限値は登録できる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();

        await service.SaveSettingsAsync(
            ReferenceCustomer,
            [new PriceChange(ReferenceProduct, 0m, null), new PriceChange(OtherProduct, 99_999_999_999.9999m, null)],
            []);

        Assert.Equal(0m, (await GetAsync(service, ReferenceProduct)).UnitPrice);
        Assert.Equal(99_999_999_999.9999m, (await GetAsync(service, OtherProduct)).UnitPrice);
    });

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public Task 範囲外の掛率は拒否される(int rate) => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();

        await Assert.ThrowsAsync<CustomerProductPriceValidationException>(
            () => service.SaveSettingsAsync(ReferenceCustomer, [], [new RateChange(ReferenceProduct, rate, null)]));
    });

    [Fact]
    public Task 掛率0と100は登録できる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();

        await service.SaveSettingsAsync(
            ReferenceCustomer, [], [new RateChange(ReferenceProduct, 0, null), new RateChange(OtherProduct, 100, null)]);

        Assert.Equal(0, (await GetAsync(service, ReferenceProduct)).Rate);
        Assert.Equal(100, (await GetAsync(service, OtherProduct)).Rate);
    });

    [Fact]
    public Task 競合する行があると同時に送った正しい変更も保存されない() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();
        await service.SaveSettingsAsync(ReferenceCustomer, [new PriceChange(ReferenceProduct, 100m, null)], []);
        var loaded = await GetAsync(service, ReferenceProduct);
        var stale = (byte[])loaded.PriceRowVersion!.Clone();
        // 他のユーザーが同じ行を更新した。
        await service.SaveSettingsAsync(ReferenceCustomer, [new PriceChange(ReferenceProduct, 150m, loaded.PriceRowVersion)], []);

        await Assert.ThrowsAsync<CustomerProductPriceConcurrencyException>(
            () => service.SaveSettingsAsync(
                ReferenceCustomer,
                [new PriceChange(OtherProduct, 999m, null), new PriceChange(ReferenceProduct, 300m, stale)],
                []));

        Assert.Null((await GetAsync(service, OtherProduct)).UnitPrice);
        Assert.Equal(150m, (await GetAsync(service, ReferenceProduct)).UnitPrice);
    });

    [Fact]
    public Task 読込後に他のユーザーが新規登録した行は競合として拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerProductPriceService>();
        await service.SaveSettingsAsync(ReferenceCustomer, [new PriceChange(ReferenceProduct, 100m, null)], []);

        // 読込時は未登録（RowVersion なし）だった行に、すでに別のユーザーの登録がある。
        await Assert.ThrowsAsync<CustomerProductPriceConcurrencyException>(
            () => service.SaveSettingsAsync(ReferenceCustomer, [new PriceChange(ReferenceProduct, 200m, null)], []));
    });

    private static async Task<CustomerProductSetting> GetAsync(CustomerProductPriceService service, string productCode)
        => (await service.GetSettingsAsync(ReferenceCustomer)).Single(s => s.ProductCode == productCode);

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
