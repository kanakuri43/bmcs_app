using bmcs_app.Application.Billing;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Billing;

/// <summary>
/// 締め解除処理（TODO.md 6-2）の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する
/// （docs/architecture.md 16章）。完了条件「解除→再締めで金額が一致する」の実証。
/// </summary>
/// <remarks>
/// <see cref="BillingClosingServiceTests"/>と同じ「専用のテスト得意先で確定した後、
/// finallyで物理削除する」方式を採る。seedの得意先・<see cref="BillingClosingServiceTests"/>の
/// 専用得意先（closing_day = 15）とも重ならない<c>closing_day = 16</c>を使う。
/// </remarks>
public class BillingReleaseServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerCode = "__TSTREL1";
    private const byte TestClosingDay = 16;

    [Fact]
    public async Task 確定済み請求データを解除すると解除済になり紐づく売上が未請求に戻る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTREL_SLP01";
        dbContext.Sales.Add(NewSalesLine(slip, 1, new DateOnly(2025, 7, 1), quantity: 5m, unitPrice: 1000m));
        await dbContext.SaveChangesAsync();

        try
        {
            var confirmed = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var target = Assert.Single(confirmed, r => r.CustomerCode == CustomerCode);
            var billingNumber = target.BillingNumber!;

            var released = await releaseService.ReleaseAsync(billingNumber);

            Assert.Equal(BillingStatus.Released, released.BillingStatus);
            Assert.NotNull(released.ReleasedAt);
            Assert.NotNull(released.ReleasedBy);

            var persistedBilling = await dbContext.Billings.AsNoTracking()
                .SingleAsync(b => b.BillingNumber == billingNumber);
            Assert.Equal(BillingStatus.Released, persistedBilling.BillingStatus);

            var persistedSales = await dbContext.Sales.AsNoTracking()
                .SingleAsync(s => s.SalesSlipNumber == slip);
            Assert.Null(persistedSales.BillingNumber);
            Assert.Equal(BillingLinkStatus.Unbilled, persistedSales.BillingStatus);
        }
        finally
        {
            await CleanupAsync(dbContext, CustomerCode, [slip]);
        }
    }

    [Fact]
    public async Task 締め解除すると解除対象の売上行の消込状態も未消込へ戻る()
    {
        // TODO.md 7-1レビューで発見した既存不整合の修正確認: 解除前にbilling_numberが外れる
        // ことだけを見ていたため、消込キャッシュ列（settlement_status/settled_amount）が
        // 消込完了のまま取り残されていた。BillingReleaseService.ReleaseAsyncに
        // SettlementService.RecalculateForCustomerAsyncを配線したことで解消したことを確認する。
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTREL_SETTLED";
        dbContext.Sales.Add(NewSalesLine(slip, 1, new DateOnly(2025, 7, 1), quantity: 5m, unitPrice: 1000m));
        await dbContext.SaveChangesAsync();

        try
        {
            var confirmed = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var billingNumber = Assert.Single(confirmed, r => r.CustomerCode == CustomerCode).BillingNumber!;

            // 実際の入金（Phase 7-2未実装）を経ずに、消込完了済みの状態を直接再現する。
            var salesLine = await dbContext.Sales.SingleAsync(s => s.SalesSlipNumber == slip);
            salesLine.SettlementStatus = SettlementStatus.FullySettled;
            salesLine.SettledAmount = salesLine.Amount;
            await dbContext.SaveChangesAsync();

            await releaseService.ReleaseAsync(billingNumber);

            var persistedSales = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slip);
            Assert.Equal(SettlementStatus.Unsettled, persistedSales.SettlementStatus);
            Assert.Equal(0m, persistedSales.SettledAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, CustomerCode, [slip]);
        }
    }

    [Fact]
    public async Task 解除済みの請求データを再度解除しようとすると例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTREL_SLP02";
        dbContext.Sales.Add(NewSalesLine(slip, 1, new DateOnly(2025, 7, 1), quantity: 1m, unitPrice: 1000m));
        await dbContext.SaveChangesAsync();

        try
        {
            var confirmed = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var billingNumber = Assert.Single(confirmed, r => r.CustomerCode == CustomerCode).BillingNumber!;

            await releaseService.ReleaseAsync(billingNumber);

            await Assert.ThrowsAsync<BillingReleaseException>(() => releaseService.ReleaseAsync(billingNumber));
        }
        finally
        {
            await CleanupAsync(dbContext, CustomerCode, [slip]);
        }
    }

    [Fact]
    public async Task 存在しない請求番号を指定すると例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (_, _, releaseService) = Resolve(scope);

        await Assert.ThrowsAsync<BillingReleaseException>(
            () => releaseService.ReleaseAsync("__TSTREL_NOTEXIST"));
    }

    [Fact]
    public async Task より新しい確定済み請求データがある場合は古い方を解除できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slipA = "__TSTREL_SEQ_A";
        var slipB = "__TSTREL_SEQ_B";
        dbContext.Sales.Add(NewSalesLine(slipA, 1, new DateOnly(2025, 7, 1), quantity: 10m, unitPrice: 1000m));
        await dbContext.SaveChangesAsync();

        try
        {
            var resultsA = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var billingA = Assert.Single(resultsA, r => r.CustomerCode == CustomerCode).BillingNumber!;

            dbContext.Sales.Add(NewSalesLine(slipB, 1, new DateOnly(2025, 8, 1), quantity: 1m, unitPrice: 2000m));
            await dbContext.SaveChangesAsync();

            var resultsB = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 8, 15));
            var billingB = Assert.Single(resultsB, r => r.CustomerCode == CustomerCode).BillingNumber!;

            // billingA（古い方）はbillingB（新しい方）が確定済みのままだと解除できない。
            var ex = await Assert.ThrowsAsync<BillingReleaseException>(() => releaseService.ReleaseAsync(billingA));
            Assert.Contains(billingB, ex.Message);

            // billingB（最新）はそのまま解除できる。
            var releasedB = await releaseService.ReleaseAsync(billingB);
            Assert.Equal(BillingStatus.Released, releasedB.BillingStatus);

            // billingBが解除されたことで、billingAが再び最新の確定済みとなり解除できる。
            var releasedA = await releaseService.ReleaseAsync(billingA);
            Assert.Equal(BillingStatus.Released, releasedA.BillingStatus);
        }
        finally
        {
            await CleanupAsync(dbContext, CustomerCode, [slipA, slipB]);
        }
    }

    [Fact]
    public async Task 解除してから同条件で再締めすると同じ金額で確定できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTREL_ROUNDTRIP";
        dbContext.Sales.Add(NewSalesLine(slip, 1, new DateOnly(2025, 7, 1), quantity: 3m, unitPrice: 1234m));
        await dbContext.SaveChangesAsync();

        try
        {
            var firstResults = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var first = Assert.Single(firstResults, r => r.CustomerCode == CustomerCode);
            var firstBillingNumber = first.BillingNumber!;

            await releaseService.ReleaseAsync(firstBillingNumber);

            var secondResults = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var second = Assert.Single(secondResults, r => r.CustomerCode == CustomerCode);

            Assert.Null(second.SkipReason);
            Assert.NotNull(second.BillingNumber);
            Assert.NotEqual(firstBillingNumber, second.BillingNumber); // 再締めでは新番号を採番する

            Assert.Equal(first.PreviousBalance, second.PreviousBalance);
            Assert.Equal(first.SalesAmount, second.SalesAmount);
            Assert.Equal(first.TaxAmount, second.TaxAmount);
            Assert.Equal(first.CurrentBillingAmount, second.CurrentBillingAmount);

            var persistedSales = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slip);
            Assert.Equal(second.BillingNumber, persistedSales.BillingNumber);
        }
        finally
        {
            await CleanupAsync(dbContext, CustomerCode, [slip]);
        }
    }

    private static (BmcsDbContext DbContext, BillingClosingService ClosingService, BillingReleaseService ReleaseService) Resolve(
        AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<BillingClosingService>(),
        scope.ServiceProvider.GetRequiredService<BillingReleaseService>());

    private static async Task InsertCustomerAsync(BmcsDbContext dbContext, string customerCode)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            SalesEmployeeCode = "EMP001",
            ClosingDay = TestClosingDay,
            TaxUnit = TaxUnit.Invoice,
            RoundingType = RoundingType.Floor,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await dbContext.SaveChangesAsync();
    }

    private static SalesEntity NewSalesLine(
        string slipNumber, short lineNumber, DateOnly slipDate, decimal quantity, decimal unitPrice)
    {
        var amount = ConsumptionTaxCalculator.CalculateLineAmount(quantity, unitPrice, RoundingType.Floor);
        var now = DateTime.Now;

        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = lineNumber,
            SlipDate = slipDate,
            CustomerCode = CustomerCode,
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "テスト用得意先",
            SlipType = SlipType.Sales,
            ProductCode = "PRD001",
            ProductName = "テスト用商品",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = amount,
            CostPrice = 700m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            SlipTaxAmount = null,
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Unbilled,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            BillingNumber = null,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    private static async Task CleanupAsync(
        BmcsDbContext dbContext, string customerCode, IReadOnlyList<string> salesSlipNumbers)
    {
        foreach (var slipNumber in salesSlipNumbers)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.sales WHERE sales_slip_number = {slipNumber}");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.billing WHERE customer_code = {customerCode}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.customer WHERE customer_code = {customerCode}");
    }
}
