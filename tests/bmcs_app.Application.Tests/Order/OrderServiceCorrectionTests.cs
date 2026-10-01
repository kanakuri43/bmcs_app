using bmcs_app.Application.Common;
using bmcs_app.Application.Order;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Order;

/// <summary>
/// 受注の直接修正（TODO.md 4-6）の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する
/// （docs/architecture.md 16章）。完了条件「未売上の受注のみ修正・保存できる」の実証。
/// </summary>
/// <remarks>
/// <see cref="OrderService.UpdateAsync"/> は明示トランザクションを開始しない（設計判断は
/// <see cref="OrderService"/> のXMLドキュメント参照）ため、<see cref="OrderStatusServiceTests"/> と
/// 同じ「外側を <c>BeginTransactionAsync</c> で開始し <c>RollbackAsync</c> する」方式が使える
/// （<see cref="OrderService.CreateAsync"/> は内部でトランザクションを開始するため、テスト用の
/// 前提データはそれを呼ばず直接INSERTする）。seedデータ（<c>order_slip</c>）には触れない。
/// </remarks>
public class OrderServiceCorrectionTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerCode = "1001";
    private const string ProductCode = "1003";

    [Fact]
    public async Task 数量と単価の変更が反映される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORDCORR01";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m));

            var updated = NewLine(orderSlipNumber, orderQuantity: 20m, unitPrice: 1500m);
            await service.UpdateAsync(orderSlipNumber, [updated], loadedLineNumbers: [1]);

            var persisted = await ReloadAsync(dbContext, orderSlipNumber, 1);
            Assert.Equal(20m, persisted.OrderQuantity);
            Assert.Equal(1500m, persisted.UnitPrice);
            Assert.False(persisted.IsDeleted);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 行追加がMax行番号1増しで採番される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORDCORR02";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m));

            var kept = NewLine(orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m);
            var added = NewLine(orderSlipNumber, orderQuantity: 5m, unitPrice: 500m, lineNumber: 0);
            await service.UpdateAsync(orderSlipNumber, [kept, added], loadedLineNumbers: [1]);

            var persisted = await ReloadAllAsync(dbContext, orderSlipNumber);
            Assert.Equal(2, persisted.Count);
            Assert.Equal(2, persisted[1].LineNumber);
            Assert.Equal(OrderStatus.NotSold, persisted[1].OrderStatus);
            Assert.Equal(0m, persisted[1].SalesConfirmedQuantity);
            Assert.Equal(0m, persisted[1].AllocatedQuantity);
            Assert.False(persisted[1].IsDeleted);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 行削除で論理削除され既存行番号は変わらない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORDCORR03";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m, lineNumber: 1));
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 5m, unitPrice: 500m, lineNumber: 2));

            var kept = NewLine(orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m, lineNumber: 1);
            await service.UpdateAsync(orderSlipNumber, [kept], loadedLineNumbers: [1, 2]);

            var line1 = await ReloadAsync(dbContext, orderSlipNumber, 1);
            var line2 = await ReloadAsync(dbContext, orderSlipNumber, 2);
            Assert.False(line1.IsDeleted);
            Assert.True(line2.IsDeleted);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 全行削除は拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORDCORR04";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m));

            await Assert.ThrowsAsync<OrderOperationException>(
                () => service.UpdateAsync(orderSlipNumber, [], loadedLineNumbers: [1]));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 一部売上の受注は修正できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORDCORR05";
            await InsertLineAsync(dbContext, NewLine(
                orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m,
                orderStatus: OrderStatus.PartiallySold, salesConfirmedQuantity: 5m));

            var updated = NewLine(orderSlipNumber, orderQuantity: 20m, unitPrice: 1000m);
            await Assert.ThrowsAsync<OrderOperationException>(
                () => service.UpdateAsync(orderSlipNumber, [updated], loadedLineNumbers: [1]));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 売上完了の受注は修正できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORDCORR06";
            await InsertLineAsync(dbContext, NewLine(
                orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m,
                orderStatus: OrderStatus.FullySold, salesConfirmedQuantity: 10m));

            var updated = NewLine(orderSlipNumber, orderQuantity: 20m, unitPrice: 1000m);
            await Assert.ThrowsAsync<OrderOperationException>(
                () => service.UpdateAsync(orderSlipNumber, [updated], loadedLineNumbers: [1]));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 中止済みの受注は修正できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORDCORR07";
            await InsertLineAsync(dbContext, NewLine(
                orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m,
                orderStatus: OrderStatus.Cancelled, salesConfirmedQuantity: 0m));

            var updated = NewLine(orderSlipNumber, orderQuantity: 20m, unitPrice: 1000m);
            await Assert.ThrowsAsync<OrderOperationException>(
                () => service.UpdateAsync(orderSlipNumber, [updated], loadedLineNumbers: [1]));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 存在しない明細行番号の指定は拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORDCORR08";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m, lineNumber: 1));

            var updated = NewLine(orderSlipNumber, orderQuantity: 20m, unitPrice: 1000m, lineNumber: 99);
            await Assert.ThrowsAsync<OrderOperationException>(
                () => service.UpdateAsync(orderSlipNumber, [updated], loadedLineNumbers: [1]));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 他ユーザーが明細行を追加していた場合は排他エラーになる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORDCORR09";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m, lineNumber: 1));

            // 別ユーザーが行を追加した状況を再現する。
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 5m, unitPrice: 500m, lineNumber: 2));

            var updated = NewLine(orderSlipNumber, orderQuantity: 20m, unitPrice: 1000m, lineNumber: 1);
            await Assert.ThrowsAsync<SlipConcurrencyException>(
                () => service.UpdateAsync(orderSlipNumber, [updated], loadedLineNumbers: [1]));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 担当者は未設定から設定し直しても解除しても反映される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORDCORR11";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m, lineNumber: 1));
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 5m, unitPrice: 500m, lineNumber: 2));
            Assert.All(await ReloadAllAsync(dbContext, orderSlipNumber), l => Assert.Null(l.EmployeeCode));

            // 担当者を設定する（伝票単位の値として全行に同じ値が入る）
            var withEmployee = new[]
            {
                NewLine(orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m, lineNumber: 1),
                NewLine(orderSlipNumber, orderQuantity: 5m, unitPrice: 500m, lineNumber: 2),
            };
            foreach (var line in withEmployee)
            {
                line.EmployeeCode = "101";
            }

            await service.UpdateAsync(orderSlipNumber, withEmployee, loadedLineNumbers: [1, 2]);
            Assert.All(await ReloadAllAsync(dbContext, orderSlipNumber), l => Assert.Equal("101", l.EmployeeCode));

            // 別の担当者へ変更する
            foreach (var line in withEmployee)
            {
                line.EmployeeCode = "102";
            }

            await service.UpdateAsync(orderSlipNumber, withEmployee, loadedLineNumbers: [1, 2]);
            Assert.All(await ReloadAllAsync(dbContext, orderSlipNumber), l => Assert.Equal("102", l.EmployeeCode));

            // 担当者は任意。未設定（NULL）へ戻せる
            foreach (var line in withEmployee)
            {
                line.EmployeeCode = null;
            }

            await service.UpdateAsync(orderSlipNumber, withEmployee, loadedLineNumbers: [1, 2]);
            Assert.All(await ReloadAllAsync(dbContext, orderSlipNumber), l => Assert.Null(l.EmployeeCode));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 得意先コードは上書きされない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string orderSlipNumber = "__TESTORDCORR10";
            await InsertLineAsync(dbContext, NewLine(orderSlipNumber, orderQuantity: 10m, unitPrice: 1000m));

            var updated = NewLine(orderSlipNumber, orderQuantity: 20m, unitPrice: 1000m);
            updated.CustomerCode = "1002"; // 変更を試みても無視される
            await service.UpdateAsync(orderSlipNumber, [updated], loadedLineNumbers: [1]);

            var persisted = await ReloadAsync(dbContext, orderSlipNumber, 1);
            Assert.Equal(CustomerCode, persisted.CustomerCode);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static (BmcsDbContext DbContext, OrderService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<OrderService>());

    private static Task InsertLineAsync(BmcsDbContext dbContext, OrderSlip line)
    {
        dbContext.OrderSlips.Add(line);
        return dbContext.SaveChangesAsync();
    }

    private static Task<OrderSlip> ReloadAsync(BmcsDbContext dbContext, string orderSlipNumber, short lineNumber) =>
        dbContext.OrderSlips.AsNoTracking()
            .SingleAsync(o => o.OrderSlipNumber == orderSlipNumber && o.LineNumber == lineNumber);

    private static Task<List<OrderSlip>> ReloadAllAsync(BmcsDbContext dbContext, string orderSlipNumber) =>
        dbContext.OrderSlips.AsNoTracking()
            .Where(o => o.OrderSlipNumber == orderSlipNumber)
            .OrderBy(o => o.LineNumber)
            .ToListAsync();

    private static OrderSlip NewLine(
        string orderSlipNumber,
        decimal orderQuantity,
        decimal unitPrice,
        short lineNumber = 1,
        OrderStatus orderStatus = OrderStatus.NotSold,
        decimal salesConfirmedQuantity = 0m) => new()
    {
        OrderSlipNumber = orderSlipNumber,
        LineNumber = lineNumber,
        OrderDate = DateOnly.FromDateTime(DateTime.Today),
        CustomerCode = CustomerCode,
        CustomerName = "テスト用得意先",
        ProductCode = ProductCode,
        ProductName = "テスト用商品",
        OrderQuantity = orderQuantity,
        UnitPrice = unitPrice,
        Amount = orderQuantity * unitPrice,
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
