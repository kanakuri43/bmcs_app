using bmcs_app.Application.Sales;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BillingEntity = bmcs_app.Domain.Entities.Billing;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Sales;

/// <summary>
/// 請求締め済み期間への売上の新規登録・日付変更を防ぐ入口バリデーション（申し送り事項R2の解消。
/// docs/design_document.md 21-4章）の結合テスト。開発用ライブDBに対して実行する
/// （docs/architecture.md 16章）。<see cref="SalesServiceTests"/>とは別に、使い捨ての得意先・
/// 確定済みbillingを用意できる専用テストクラスとする（seed済みのCUS001は既に確定済みbillingを
/// 持つため、境界値ちょうどのシナリオを組み立てにくい）。
/// </summary>
public class SalesServiceBillingClosedDateTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    [Fact]
    public async Task 請求締め済み期間の日付では売上を新規登録できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTSDL01";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(dbContext, "__TSTBIL_SDL01", customerCode, new DateOnly(2026, 9, 30));

            var exSame = await Assert.ThrowsAsync<SalesOperationException>(
                () => service.CreateAsync([NewLine(customerCode, new DateOnly(2026, 9, 30))], RoundingType.Floor));
            Assert.Contains("請求締め済み", exSame.Message);

            var exBefore = await Assert.ThrowsAsync<SalesOperationException>(
                () => service.CreateAsync([NewLine(customerCode, new DateOnly(2026, 9, 29))], RoundingType.Floor));
            Assert.Contains("請求締め済み", exBefore.Message);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 請求締切日の翌日の売上は登録できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTSDL02";
        string? salesSlipNumber = null;
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(dbContext, "__TSTBIL_SDL02", customerCode, new DateOnly(2026, 9, 30));

            salesSlipNumber = await service.CreateAsync(
                [NewLine(customerCode, new DateOnly(2026, 10, 1))], RoundingType.Floor);

            Assert.Equal(8, salesSlipNumber.Length);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode, salesSlipNumber);
        }
    }

    [Fact]
    public async Task 確定済み請求が無い得意先は過去日付でも売上を登録できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTSDL03";
        string? salesSlipNumber = null;
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            salesSlipNumber = await service.CreateAsync(
                [NewLine(customerCode, new DateOnly(2020, 1, 1))], RoundingType.Floor);

            Assert.Equal(8, salesSlipNumber.Length);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode, salesSlipNumber);
        }
    }

    [Fact]
    public async Task 都度得意先は請求締めの影響を受けず登録できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTSDL04";
        string? salesSlipNumber = null;
        try
        {
            await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Line);

            var line = NewLine(customerCode, new DateOnly(2020, 1, 1));
            line.TaxUnit = TaxUnit.Line;
            salesSlipNumber = await service.CreateAsync([line], RoundingType.Floor);

            Assert.Equal(8, salesSlipNumber.Length);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode, salesSlipNumber);
        }
    }

    [Fact]
    public async Task 訂正で伝票日付を請求締め済み期間へ戻すことはできない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTSDL05";
        string? salesSlipNumber = null;
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            // 実際の業務順序（売上登録 → その後に請求締めが実行され集計期間に呑み込まれる）を再現するため、
            // 先に売上を登録してから確定済みbillingを挿入する（日付制限の実装後、逆順だと売上登録自体が
            // 拒否されるため）。
            salesSlipNumber = await service.CreateAsync(
                [NewLine(customerCode, new DateOnly(2026, 10, 5))], RoundingType.Floor);
            await InsertBillingAsync(dbContext, "__TSTBIL_SDL05", customerCode, new DateOnly(2026, 9, 30));

            var current = await dbContext.Sales
                .Where(s => s.SalesSlipNumber == salesSlipNumber && !s.IsDeleted)
                .ToListAsync();
            var loadedLineNumbers = current.Select(l => l.LineNumber).ToList();

            var incoming = NewLine(customerCode, new DateOnly(2026, 9, 30));
            incoming.LineNumber = current[0].LineNumber;

            var ex = await Assert.ThrowsAsync<SalesOperationException>(() => service.UpdateAsync(
                salesSlipNumber, [incoming], RoundingType.Floor, loadedLineNumbers));
            Assert.Contains("請求締め済み", ex.Message);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode, salesSlipNumber);
        }
    }

    private static (BmcsDbContext DbContext, SalesService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<SalesService>());

    private static SalesEntity NewLine(string customerCode, DateOnly slipDate) => new()
    {
        SalesSlipNumber = string.Empty,
        LineNumber = 0,
        SlipDate = slipDate,
        CustomerCode = customerCode,
        TaxUnit = TaxUnit.Invoice,
        CustomerName = "テスト用得意先",
        SlipType = SlipType.Sales,
        ProductCode = "1001",
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
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static Task InsertCustomerAsync(
        BmcsDbContext dbContext, string customerCode, TaxUnit taxUnit = TaxUnit.Invoice)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new CustomerEntity
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = taxUnit == TaxUnit.Line ? (byte)0 : (byte)15,
            TaxUnit = taxUnit,
            RoundingType = RoundingType.Floor,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static Task InsertBillingAsync(
        BmcsDbContext dbContext, string billingNumber, string customerCode, DateOnly billingDate)
    {
        var now = DateTime.Now;
        dbContext.Billings.Add(new BillingEntity
        {
            BillingNumber = billingNumber,
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "テスト用得意先",
            BillingDate = billingDate,
            ClosingYearMonth = $"{billingDate.Year:D4}{billingDate.Month:D2}",
            PreviousBalance = 0m,
            ReceiptAmount = 0m,
            SalesAmount = 1000m,
            TaxAmount = 0m,
            CurrentBillingAmount = 1000m,
            StandardRateTaxableAmount = 0m,
            StandardRateTaxAmount = 0m,
            ReducedRateTaxableAmount = 0m,
            ReducedRateTaxAmount = 0m,
            TaxExemptAmount = 0m,
            BillingStatus = BillingStatus.Confirmed,
            ConfirmedAt = now,
            ConfirmedBy = "TEST",
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static async Task CleanupAsync(BmcsDbContext dbContext, string customerCode, string? salesSlipNumber = null)
    {
        if (salesSlipNumber is not null)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.sales WHERE sales_slip_number = {salesSlipNumber}");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.billings WHERE customer_code = {customerCode}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.customers WHERE customer_code = {customerCode}");
    }
}
