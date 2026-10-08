using bmcs_app.Application.Order;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Order;

/// <summary>
/// 受注の状態遷移の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する
/// （docs/architecture.md 16章）。完了条件「分納・中止・売上取消による逆遷移が正しく反映される」の実証。
/// </summary>
/// <remarks>
/// 各テストは <c>BeginTransactionAsync</c> で開始し、テスト用の受注行を一時的にINSERTして
/// 検証した後、必ず <c>RollbackAsync</c> する（<c>SlipNumberServiceTests</c> と同じ方式）。
/// <see cref="OrderStatusService.ApplySalesQuantityDeltasAsync"/> は明示トランザクションを要求する
/// ため相性が良い。seed データ（<c>order_slip</c> のORD001〜ORD004）には触れない。
/// </remarks>
public class OrderStatusServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerCode = "CUS001";
    private const string ProductCode = "PRD001";

    // ---- 分納 ----

    [Fact]
    public async Task 未売上から一部の数量を売上化すると一部売上になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORD01";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, salesConfirmedQuantity: 0m, OrderStatus.NotSold));

            await service.ApplySalesQuantityDeltasAsync([new OrderLineQuantityDelta(orderSlipNumber, 1, 4m)]);
            await dbContext.SaveChangesAsync();

            var persisted = await ReloadAsync(dbContext, orderSlipNumber, 1);
            Assert.Equal(4m, persisted.SalesConfirmedQuantity);
            Assert.Equal(OrderStatus.PartiallySold, persisted.OrderStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 一部売上から残数量を売上化すると売上完了になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORD02";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, salesConfirmedQuantity: 4m, OrderStatus.PartiallySold));

            await service.ApplySalesQuantityDeltasAsync([new OrderLineQuantityDelta(orderSlipNumber, 1, 6m)]);
            await dbContext.SaveChangesAsync();

            var persisted = await ReloadAsync(dbContext, orderSlipNumber, 1);
            Assert.Equal(10m, persisted.SalesConfirmedQuantity);
            Assert.Equal(OrderStatus.FullySold, persisted.OrderStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    // ---- 逆遷移（売上取消） ----

    [Fact]
    public async Task 売上完了から順に取消すと一部売上を経て未売上に戻る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORD03";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, salesConfirmedQuantity: 10m, OrderStatus.FullySold));

            await service.ApplySalesQuantityDeltasAsync([new OrderLineQuantityDelta(orderSlipNumber, 1, -6m)]);
            await dbContext.SaveChangesAsync();

            var afterFirst = await ReloadAsync(dbContext, orderSlipNumber, 1);
            Assert.Equal(4m, afterFirst.SalesConfirmedQuantity);
            Assert.Equal(OrderStatus.PartiallySold, afterFirst.OrderStatus);

            await service.ApplySalesQuantityDeltasAsync([new OrderLineQuantityDelta(orderSlipNumber, 1, -4m)]);
            await dbContext.SaveChangesAsync();

            var afterSecond = await ReloadAsync(dbContext, orderSlipNumber, 1);
            Assert.Equal(0m, afterSecond.SalesConfirmedQuantity);
            Assert.Equal(OrderStatus.NotSold, afterSecond.OrderStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    // ---- 異常系（デルタ） ----

    [Fact]
    public async Task 受注数量を超える売上化は拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORD04";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, salesConfirmedQuantity: 8m, OrderStatus.PartiallySold));

            await Assert.ThrowsAsync<OrderOperationException>(
                () => service.ApplySalesQuantityDeltasAsync([new OrderLineQuantityDelta(orderSlipNumber, 1, 5m)]));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 売上化済数量を負にする取消は拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORD05";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, salesConfirmedQuantity: 3m, OrderStatus.PartiallySold));

            await Assert.ThrowsAsync<OrderOperationException>(
                () => service.ApplySalesQuantityDeltasAsync([new OrderLineQuantityDelta(orderSlipNumber, 1, -5m)]));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 中止済みの明細行は売上化できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORD06";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, salesConfirmedQuantity: 0m, OrderStatus.Cancelled));

            await Assert.ThrowsAsync<OrderOperationException>(
                () => service.ApplySalesQuantityDeltasAsync([new OrderLineQuantityDelta(orderSlipNumber, 1, 1m)]));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 存在しない受注明細行へのデルタは拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await Assert.ThrowsAsync<OrderOperationException>(
                () => service.ApplySalesQuantityDeltasAsync([new OrderLineQuantityDelta("__TESTORD_NONE", 1, 1m)]));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 同一明細行に対するデルタの重複指定は拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORD07";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, salesConfirmedQuantity: 0m, OrderStatus.NotSold));

            await Assert.ThrowsAsync<OrderOperationException>(
                () => service.ApplySalesQuantityDeltasAsync(
                [
                    new OrderLineQuantityDelta(orderSlipNumber, 1, 1m),
                    new OrderLineQuantityDelta(orderSlipNumber, 1, 2m),
                ]));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task トランザクション外での売上化済数量の更新は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (_, service) = Resolve(scope);

        // BeginTransactionAsync を呼んでいない状態で更新を試みる。
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplySalesQuantityDeltasAsync([new OrderLineQuantityDelta("__TESTORD_NONE", 1, 1m)]));
    }

    // ---- 中止 ----

    [Fact]
    public async Task 未売上の受注を中止すると全行が中止になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORD08";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, salesConfirmedQuantity: 0m, OrderStatus.NotSold));

            await service.CancelSlipAsync(orderSlipNumber);

            var persisted = await ReloadAsync(dbContext, orderSlipNumber, 1);
            Assert.Equal(OrderStatus.Cancelled, persisted.OrderStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 一部売上の受注も中止でき売上化済数量は保持される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORD09";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, salesConfirmedQuantity: 3m, OrderStatus.PartiallySold));

            await service.CancelSlipAsync(orderSlipNumber);

            var persisted = await ReloadAsync(dbContext, orderSlipNumber, 1);
            Assert.Equal(OrderStatus.Cancelled, persisted.OrderStatus);
            Assert.Equal(3m, persisted.SalesConfirmedQuantity); // 分納済みの実績は消さない
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 複数行の受注を中止すると全行が一括で中止になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORD10";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, salesConfirmedQuantity: 0m, OrderStatus.NotSold, lineNumber: 1));
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 5m, salesConfirmedQuantity: 2m, OrderStatus.PartiallySold, lineNumber: 2));

            await service.CancelSlipAsync(orderSlipNumber);

            var line1 = await ReloadAsync(dbContext, orderSlipNumber, 1);
            var line2 = await ReloadAsync(dbContext, orderSlipNumber, 2);
            Assert.Equal(OrderStatus.Cancelled, line1.OrderStatus);
            Assert.Equal(OrderStatus.Cancelled, line2.OrderStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    // ---- 異常系（中止） ----

    [Fact]
    public async Task 売上完了済みの受注は中止できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORD11";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, salesConfirmedQuantity: 10m, OrderStatus.FullySold));

            await Assert.ThrowsAsync<OrderOperationException>(() => service.CancelSlipAsync(orderSlipNumber));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 既に中止済みの受注を再度中止しようとすると拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORD12";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, salesConfirmedQuantity: 0m, OrderStatus.Cancelled));

            await Assert.ThrowsAsync<OrderOperationException>(() => service.CancelSlipAsync(orderSlipNumber));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 存在しない受注番号の中止は拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await Assert.ThrowsAsync<OrderOperationException>(() => service.CancelSlipAsync("__TESTORD_NONE"));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static (BmcsDbContext DbContext, OrderStatusService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<OrderStatusService>());

    private static Task InsertLineAsync(BmcsDbContext dbContext, OrderSlip line)
    {
        dbContext.OrderSlips.Add(line);
        return dbContext.SaveChangesAsync();
    }

    private static Task<OrderSlip> ReloadAsync(BmcsDbContext dbContext, string orderSlipNumber, short lineNumber) =>
        dbContext.OrderSlips.AsNoTracking()
            .SingleAsync(o => o.OrderSlipNumber == orderSlipNumber && o.LineNumber == lineNumber);

    private static OrderSlip NewLine(
        string orderSlipNumber,
        decimal orderQuantity,
        decimal salesConfirmedQuantity,
        OrderStatus orderStatus,
        short lineNumber = 1) => new()
    {
        OrderSlipNumber = orderSlipNumber,
        LineNumber = lineNumber,
        OrderDate = DateOnly.FromDateTime(DateTime.Today),
        CustomerCode = CustomerCode,
        CustomerName = "テスト用得意先",
        ProductCode = ProductCode,
        ProductName = "テスト用商品",
        OrderQuantity = orderQuantity,
        UnitPrice = 1000m,
        Amount = orderQuantity * 1000m,
        CostPrice = 700m,
        TaxCategory = TaxCategory.Standard,
        TaxRate = 10m,
        AllocatedQuantity = 0m,
        OrderStatus = orderStatus,
        SalesConfirmedQuantity = salesConfirmedQuantity,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };
}
