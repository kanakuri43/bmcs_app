using bmcs_app.Application.Common;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 消費税率マスタの結合テスト。外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// 実データの行（2019-10-01 など）と重ならないよう、テスト用の施行日は 2099 年を使う。
/// </summary>
public class TaxRateServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private static readonly DateOnly April = new(2099, 4, 1);
    private static readonly DateOnly October = new(2099, 10, 1);

    [Fact]
    public Task 登録_更新_削除ができる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<TaxRateService>();
        var query = sp.GetRequiredService<TaxRateQueryService>();

        var created = await service.CreateAsync(April, 11m, 9m);
        Assert.Contains(await query.GetAllAsync(), m => m.EffectiveDate == April && m.StandardTaxRate == 11m);

        var updated = await service.UpdateAsync(April, 12.5m, 7m, created.RowVersion!);
        Assert.Equal(12.5m, updated.StandardTaxRate);
        Assert.Equal(7m, updated.ReducedTaxRate);

        await service.DeactivateAsync(April, updated.RowVersion!);
        Assert.DoesNotContain(await query.GetAllAsync(), m => m.EffectiveDate == April);
    });

    [Fact]
    public Task 同じ施行日の重複は拒否され_削除済みなら復活する() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<TaxRateService>();
        var query = sp.GetRequiredService<TaxRateQueryService>();

        var created = await service.CreateAsync(April, 11m, 9m);
        await Assert.ThrowsAsync<TaxRateValidationException>(() => service.CreateAsync(April, 12m, 8m));

        await service.DeactivateAsync(April, created.RowVersion!);
        await service.CreateAsync(April, 13m, 6m);

        var revived = Assert.Single(await query.GetAllAsync(), m => m.EffectiveDate == April);
        Assert.Equal(13m, revived.StandardTaxRate);
        Assert.Equal(6m, revived.ReducedTaxRate);
    });

    [Theory]
    [InlineData(-1, 8)]
    [InlineData(100, 8)]
    [InlineData(10, 100)]
    [InlineData(10.123, 8)]
    [InlineData(10, 8.001)]
    public Task 範囲外や小数3桁以上の税率は拒否される(double standard, double reduced) => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<TaxRateService>();

        await Assert.ThrowsAsync<TaxRateValidationException>(
            () => service.CreateAsync(April, (decimal)standard, (decimal)reduced));
    });

    [Fact]
    public Task 古いRowVersionでの更新と削除は競合になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<TaxRateService>();
        var created = await service.CreateAsync(April, 11m, 9m);
        var stale = (byte[])created.RowVersion!.Clone();

        await service.UpdateAsync(April, 12m, 9m, stale);

        await Assert.ThrowsAsync<TaxRateConcurrencyException>(() => service.UpdateAsync(April, 13m, 9m, stale));
        await Assert.ThrowsAsync<TaxRateConcurrencyException>(() => service.DeactivateAsync(April, stale));
    });

    [Fact]
    public Task 最後の1件は削除できない() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<TaxRateService>();
        var dbContext = sp.GetRequiredService<BmcsDbContext>();

        // トランザクション内で、有効な行を1件だけ残す（Rollback で元に戻る）。
        var active = dbContext.TaxRateMasters.Where(m => !m.IsDeleted).OrderBy(m => m.EffectiveDate).ToList();
        foreach (var rate in active.Skip(1))
        {
            rate.IsDeleted = true;
        }

        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<TaxRateValidationException>(
            () => service.DeactivateAsync(active[0].EffectiveDate, active[0].RowVersion!));
    });

    [Fact]
    public Task 受注がある期間の税率は変更_削除_割り込み追加ができない() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<TaxRateService>();
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        var april = await service.CreateAsync(April, 11m, 9m);
        var october = await service.CreateAsync(October, 12m, 8m);

        // 4月施行の期間（2099/04/01〜2099/09/30）の中に受注を置く。
        dbContext.OrderSlips.Add(NewOrder(new DateOnly(2099, 5, 10)));
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<TaxRateValidationException>(() => service.UpdateAsync(April, 15m, 9m, april.RowVersion!));
        await Assert.ThrowsAsync<TaxRateValidationException>(() => service.DeactivateAsync(April, april.RowVersion!));
        // 5/1 施行の行を割り込ませると、5/10 の受注の税率が変わってしまう。
        await Assert.ThrowsAsync<TaxRateValidationException>(() => service.CreateAsync(new DateOnly(2099, 5, 1), 15m, 9m));

        // 伝票のない期間は触れる。
        await service.CreateAsync(new DateOnly(2099, 6, 1), 15m, 9m);
        await service.UpdateAsync(October, 13m, 8m, october.RowVersion!);
    });

    [Fact]
    public Task 売上がある期間の税率は変更_削除ができない() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<TaxRateService>();
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        var april = await service.CreateAsync(April, 11m, 9m);
        var october = await service.CreateAsync(October, 12m, 8m);

        // 10月施行の最新行（無期限）の中に売上を置く。
        dbContext.Sales.Add(NewSales(new DateOnly(2100, 1, 15)));
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<TaxRateValidationException>(() => service.UpdateAsync(October, 15m, 8m, october.RowVersion!));
        await Assert.ThrowsAsync<TaxRateValidationException>(() => service.DeactivateAsync(October, october.RowVersion!));

        await service.UpdateAsync(April, 15m, 9m, april.RowVersion!);
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

    private static OrderSlip NewOrder(DateOnly orderDate)
    {
        var now = DateTime.Now;
        return new OrderSlip
        {
            OrderSlipNumber = "__TXR01",
            LineNumber = 1,
            OrderDate = orderDate,
            CustomerCode = "CUS001",
            CustomerName = "テスト用得意先",
            ProductCode = "PRD001",
            ProductName = "テスト用商品",
            OrderQuantity = 1m,
            UnitPrice = 1000m,
            Amount = 1000m,
            CostPrice = 0m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            AllocatedQuantity = 0m,
            OrderStatus = OrderStatus.NotSold,
            SalesConfirmedQuantity = 0m,
            CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
        };
    }

    private static Domain.Entities.Sales NewSales(DateOnly slipDate)
    {
        var now = DateTime.Now;
        return new Domain.Entities.Sales
        {
            SalesSlipNumber = "__TXR02",
            LineNumber = 1,
            SlipDate = slipDate,
            CustomerCode = "CUS001",
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "テスト用得意先",
            SlipType = SlipType.Sales,
            ProductCode = "PRD001",
            ProductName = "テスト用商品",
            Quantity = 1m,
            UnitPrice = 1000m,
            Amount = 1000m,
            CostPrice = 0m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Unbilled,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
        };
    }
}
