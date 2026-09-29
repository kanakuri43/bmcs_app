using bmcs_app.Application.Order;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;

namespace bmcs_app.Application.Tests.Order;

/// <summary>
/// 伝票検索モーダル用の受注検索（<see cref="OrderQueryService.SearchAsync"/>）の結合テスト。
/// 開発用ライブDB（172.16.3.171）に対して実行し、テストの最後に必ずRollbackする。
/// </summary>
public class OrderQueryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerCode = "__TSTORQ1";

    [Fact]
    public async Task 全行が中止または売上完了の受注は除外される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerCode);
            dbContext.OrderSlips.Add(NewOrder(
                CustomerCode, "__TSTORQ_CANCEL1", 1, new DateOnly(2026, 8, 1), 1_000m, OrderStatus.Cancelled));
            dbContext.OrderSlips.Add(NewOrder(
                CustomerCode, "__TSTORQ_AVAIL1", 1, new DateOnly(2026, 8, 2), 1_000m, OrderStatus.NotSold));
            await dbContext.SaveChangesAsync();

            var results = await service.SearchAsync(CustomerCode);

            Assert.Contains(results, h => h.OrderSlipNumber == "__TSTORQ_AVAIL1");
            Assert.DoesNotContain(results, h => h.OrderSlipNumber == "__TSTORQ_CANCEL1");
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    /// <summary>
    /// 旧実装は除外判定（全行が中止・売上完了の受注を弾く）が明細行 Take(1000) の後に適用されていた。
    /// 中止済みの受注が新しい日付に大量にあると、1000行の窓がそれらで埋まり、古い日付の
    /// 売上化可能な受注が窓に入らず結果からごっそり消える。修正後はSQL側で除外を前置するため、
    /// 除外対象がどれだけあっても売上化可能な受注は取りこぼされない。
    /// </summary>
    [Fact]
    public async Task 除外対象の受注が大量にあっても売上化可能な受注は取りこぼされない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerCode);

            const int cancelledCount = 210;
            const int cancelledLinesPerSlip = 5;
            var cancelledBaseDate = new DateOnly(2026, 8, 1);
            var cancelledOrders = new List<OrderSlip>();
            for (var i = 0; i < cancelledCount; i++)
            {
                var slipNumber = $"__TSTORQ_C{i:D3}";
                var orderDate = cancelledBaseDate.AddDays(i);
                for (short lineNumber = 1; lineNumber <= cancelledLinesPerSlip; lineNumber++)
                {
                    cancelledOrders.Add(NewOrder(
                        CustomerCode, slipNumber, lineNumber, orderDate, 1_000m, OrderStatus.Cancelled));
                }
            }
            dbContext.OrderSlips.AddRange(cancelledOrders);

            const int availableCount = 3;
            var availableBaseDate = new DateOnly(2026, 1, 1);
            var availableSlipNumbers = new List<string>();
            for (var i = 0; i < availableCount; i++)
            {
                var slipNumber = $"__TSTORQ_A{i}";
                availableSlipNumbers.Add(slipNumber);
                dbContext.OrderSlips.Add(NewOrder(
                    CustomerCode, slipNumber, 1, availableBaseDate.AddDays(i), 1_000m, OrderStatus.NotSold));
            }
            await dbContext.SaveChangesAsync();

            var results = await service.SearchAsync(CustomerCode);

            foreach (var slipNumber in availableSlipNumbers)
            {
                Assert.Contains(results, h => h.OrderSlipNumber == slipNumber);
            }
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static (BmcsDbContext DbContext, OrderQueryService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<OrderQueryService>());

    private static Task InsertCustomerAsync(BmcsDbContext dbContext, string customerCode)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new CustomerEntity
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = 20,
            TaxUnit = TaxUnit.Invoice,
            RoundingType = RoundingType.Floor,
            BillingCustomerCode = customerCode,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static OrderSlip NewOrder(
        string customerCode, string slipNumber, short lineNumber, DateOnly orderDate, decimal amount,
        OrderStatus orderStatus)
    {
        var now = DateTime.Now;
        return new OrderSlip
        {
            OrderSlipNumber = slipNumber,
            LineNumber = lineNumber,
            OrderDate = orderDate,
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ProductCode = "1001",
            ProductName = "テスト用商品",
            OrderQuantity = 1m,
            UnitPrice = amount,
            Amount = amount,
            CostPrice = 0m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            AllocatedQuantity = 0m,
            OrderStatus = orderStatus,
            SalesConfirmedQuantity = 0m,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }
}
