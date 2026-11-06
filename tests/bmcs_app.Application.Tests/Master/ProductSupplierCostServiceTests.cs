using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 仕入先／商品／原価マスタの結合テスト。外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// </summary>
public class ProductSupplierCostServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string SupplierCode = "__SUP01";
    private const string ReferenceProduct = "PRD001";
    private const string OtherProduct = "PRD002";

    [Fact]
    public Task 原価を新規登録して更新できる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<ProductSupplierCostService>();

        await service.SaveSettingsAsync(SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, 123.4567m, null)]);
        var created = await GetAsync(service, ReferenceProduct);
        Assert.Equal(123.4567m, created.CostPrice);

        await service.SaveSettingsAsync(SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, 0m, created.RowVersion)]);
        var updated = await GetAsync(service, ReferenceProduct);
        Assert.Equal(0m, updated.CostPrice);
    });

    [Fact]
    public Task 一覧には有効な商品の全件が並び_標準原価も参照できる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<ProductSupplierCostService>();
        var dbContext = sp.GetRequiredService<BmcsDbContext>();

        var settings = await service.GetSettingsAsync(SupplierCode);

        var activeProductCount = await dbContext.Products.CountAsync(p => !p.IsDeleted);
        Assert.Equal(activeProductCount, settings.Count);
        var expected = (await dbContext.Products.AsNoTracking().SingleAsync(p => p.ProductCode == ReferenceProduct)).StandardCostPrice;
        Assert.Equal(expected, settings.Single(s => s.ProductCode == ReferenceProduct).StandardCostPrice);
        Assert.All(settings, s => Assert.Null(s.CostPrice));
    });

    [Fact]
    public Task 空欄で登録すると無効化され_値を入れ直すと同じ行が有効に戻る() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<ProductSupplierCostService>();
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        await service.SaveSettingsAsync(SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, 100m, null)]);
        var created = await GetAsync(service, ReferenceProduct);

        await service.SaveSettingsAsync(SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, null, created.RowVersion)]);

        var deactivated = await GetAsync(service, ReferenceProduct);
        Assert.Null(deactivated.CostPrice);
        Assert.NotNull(deactivated.RowVersion);
        Assert.True((await dbContext.ProductSupplierCosts.AsNoTracking()
            .SingleAsync(c => c.SupplierCode == SupplierCode && c.ProductCode == ReferenceProduct)).IsDeleted);

        await service.SaveSettingsAsync(SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, 250m, deactivated.RowVersion)]);
        var revived = await GetAsync(service, ReferenceProduct);
        Assert.Equal(250m, revived.CostPrice);
        Assert.Equal(1, await dbContext.ProductSupplierCosts.CountAsync(
            c => c.SupplierCode == SupplierCode && c.ProductCode == ReferenceProduct));
    });

    [Fact]
    public Task 未登録の行を空欄のまま登録しても何も作られない() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<ProductSupplierCostService>();
        var dbContext = sp.GetRequiredService<BmcsDbContext>();

        await service.SaveSettingsAsync(SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, null, null)]);

        Assert.Equal(0, await dbContext.ProductSupplierCosts.CountAsync(c => c.SupplierCode == SupplierCode));
    });

    [Theory]
    [InlineData("-0.0001")]
    [InlineData("100000000000")]
    public Task 範囲外の原価は拒否される(string cost) => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<ProductSupplierCostService>();

        await Assert.ThrowsAsync<ProductSupplierCostValidationException>(() => service.SaveSettingsAsync(
            SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, decimal.Parse(cost), null)]));
    });

    [Fact]
    public Task 無効または未登録の仕入先と商品は拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<ProductSupplierCostService>();

        await Assert.ThrowsAsync<ProductSupplierCostValidationException>(() => service.SaveSettingsAsync(
            "__NOSUP", [new ProductSupplierCostChange(ReferenceProduct, 1m, null)]));
        await Assert.ThrowsAsync<ProductSupplierCostValidationException>(() => service.SaveSettingsAsync(
            SupplierCode, [new ProductSupplierCostChange("__NOPRD", 1m, null)]));

        var suppliers = sp.GetRequiredService<SupplierService>();
        var supplier = await suppliers.GetByCodeAsync(SupplierCode);
        await suppliers.DeactivateAsync(NewSupplier(SupplierCode, supplier!.RowVersion));
        await Assert.ThrowsAsync<ProductSupplierCostValidationException>(() => service.SaveSettingsAsync(
            SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, 1m, null)]));
    });

    [Fact]
    public Task 読込後に他のユーザーが更新していたら全件拒否される() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<ProductSupplierCostService>();
        await service.SaveSettingsAsync(SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, 100m, null)]);
        var loaded = await GetAsync(service, ReferenceProduct);

        // 別の端末が先に更新した状態。
        await service.SaveSettingsAsync(SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, 150m, loaded.RowVersion)]);

        await Assert.ThrowsAsync<ProductSupplierCostConcurrencyException>(() => service.SaveSettingsAsync(
            SupplierCode,
            [
                new ProductSupplierCostChange(OtherProduct, 10m, null),
                new ProductSupplierCostChange(ReferenceProduct, 200m, loaded.RowVersion),
            ]));

        // 全件失敗なので、同時に渡した別商品の行も作られていない。
        var after = await service.GetSettingsAsync(SupplierCode);
        Assert.Null(after.Single(s => s.ProductCode == OtherProduct).CostPrice);
        Assert.Equal(150m, after.Single(s => s.ProductCode == ReferenceProduct).CostPrice);
    });

    [Fact]
    public Task 仕入先の有効な原価があればそれを_なければ標準原価を解決する() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<ProductSupplierCostService>();
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        var standard = (await dbContext.Products.AsNoTracking().SingleAsync(p => p.ProductCode == ReferenceProduct)).StandardCostPrice;

        // 仕入先なし・原価行なしは標準原価。
        Assert.Equal(standard, await service.ResolveCostAsync(ReferenceProduct, null));
        Assert.Equal(standard, await service.ResolveCostAsync(ReferenceProduct, SupplierCode));

        await service.SaveSettingsAsync(SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, 123.5m, null)]);
        Assert.Equal(123.5m, await service.ResolveCostAsync(ReferenceProduct, SupplierCode));
        Assert.Equal(standard, await service.ResolveCostAsync(ReferenceProduct, null)); // 仕入先なしは標準原価のまま

        // 原価を空欄にして無効化すると標準原価に戻る。
        var loaded = await GetAsync(service, ReferenceProduct);
        await service.SaveSettingsAsync(SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, null, loaded.RowVersion)]);
        Assert.Equal(standard, await service.ResolveCostAsync(ReferenceProduct, SupplierCode));

        await Assert.ThrowsAsync<ProductSupplierCostValidationException>(() => service.ResolveCostAsync("__NOPRD", null));
    });

    [Fact]
    public Task 初期値は主仕入先とその原価_主仕入先なしは仕入先なしの標準原価() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<ProductSupplierCostService>();
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        var product = await dbContext.Products.SingleAsync(p => p.ProductCode == ReferenceProduct);
        var standard = product.StandardCostPrice;

        product.MainSupplierCode = null;
        await dbContext.SaveChangesAsync();
        Assert.Equal((null, standard), await service.ResolveDefaultAsync(ReferenceProduct));

        // 主仕入先に原価行がなければ、仕入先は主仕入先のまま原価は標準原価。
        product.MainSupplierCode = SupplierCode;
        await dbContext.SaveChangesAsync();
        Assert.Equal((SupplierCode, standard), await service.ResolveDefaultAsync(ReferenceProduct));

        await service.SaveSettingsAsync(SupplierCode, [new ProductSupplierCostChange(ReferenceProduct, 99m, null)]);
        Assert.Equal((SupplierCode, 99m), await service.ResolveDefaultAsync(ReferenceProduct));
    });

    private static async Task<ProductSupplierCostSetting> GetAsync(ProductSupplierCostService service, string productCode)
        => (await service.GetSettingsAsync(SupplierCode)).Single(s => s.ProductCode == productCode);

    private async Task RunAsync(Func<IServiceProvider, Task> body)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await scope.ServiceProvider.GetRequiredService<SupplierService>().CreateAsync(NewSupplier(SupplierCode, null));
            await body(scope.ServiceProvider);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static Supplier NewSupplier(string code, byte[]? rowVersion) => new()
    {
        SupplierCode = code,
        SupplierName = "テスト仕入先",
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        RowVersion = rowVersion,
    };
}
