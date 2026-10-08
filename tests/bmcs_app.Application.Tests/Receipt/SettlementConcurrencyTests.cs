using bmcs_app.Application.Common;
using bmcs_app.Application.Receipt;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BillingEntity = bmcs_app.Domain.Entities.Billing;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Receipt;

/// <summary>
/// 同一請求への2つの入金の同時登録が rowversion で検出されること（docs/architecture.md 9章）の結合テスト。
/// 2つのDIスコープ（＝2つのDbContext＝2端末）で再現する。端末Aが売上行を読み込んだ後に端末Bが入金を
/// コミットし、その後に端末Aが入金を保存する。コミット済みデータを使うため最後に必ず削除する。
/// </summary>
public class SettlementConcurrencyTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    [Fact]
    public async Task 同一請求への2つの入金の同時登録は後勝ち上書きにならず競合として検出される()
    {
        const string customerCode = "__TSTCNC01";
        const string billingNumber = "__TSTBIL_CNC01";
        const string salesSlipNumber = "__TSTSAL_CNC01";

        await using var setupScope = fixture.Services.CreateAsyncScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        try
        {
            await InsertCustomerAsync(setupDb, customerCode);
            await InsertBillingAsync(setupDb, billingNumber, customerCode, 10000m);
            await InsertSalesLineAsync(setupDb, salesSlipNumber, customerCode, billingNumber, 10000m);

            await using var scopeA = fixture.Services.CreateAsyncScope();
            await using var scopeB = fixture.Services.CreateAsyncScope();
            var dbA = scopeA.ServiceProvider.GetRequiredService<BmcsDbContext>();
            var serviceA = scopeA.ServiceProvider.GetRequiredService<ReceiptEntryService>();
            var serviceB = scopeB.ServiceProvider.GetRequiredService<ReceiptEntryService>();

            // 端末Aが画面表示時点の売上行（消込前）を追跡状態で保持する。
            var staleSales = await dbA.Sales.Where(s => s.CustomerCode == customerCode).ToListAsync();
            Assert.Equal(0m, Assert.Single(staleSales).SettledAmount);

            // 端末Bが先に入金（6,000円）をコミットする。
            await serviceB.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(6000m)]);

            // 端末Aが古い売上行のまま入金（6,000円）を保存しようとすると、サイレントに上書きされず競合になる。
            await Assert.ThrowsAsync<SlipConcurrencyException>(() => serviceA.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 26), slipRemarks: null, lines: [CashLine(6000m)]));

            // Aの保存は全体がロールバックされ、DBにはBの入金だけが反映されている。
            await using var verifyScope = fixture.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<BmcsDbContext>();
            var receipts = await verifyDb.Receipts.AsNoTracking()
                .Where(r => r.CustomerCode == customerCode && !r.IsDeleted).ToListAsync();
            Assert.Equal(6000m, Assert.Single(receipts).Amount);
            var allocated = await verifyDb.ReceiptAllocations.AsNoTracking()
                .Where(a => a.CustomerCode == customerCode && !a.IsDeleted).SumAsync(a => a.AllocatedAmount);
            Assert.Equal(6000m, allocated);
            var sales = await verifyDb.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == salesSlipNumber);
            Assert.Equal(6000m, sales.SettledAmount);
            Assert.Equal(SettlementStatus.PartiallySettled, sales.SettlementStatus);
        }
        finally
        {
            await CleanupAsync(setupDb, customerCode, salesSlipNumber);
        }
    }

    private static ReceiptLineInput CashLine(decimal amount) => new("CASH", null, null, amount, null);

    private static Task InsertCustomerAsync(
        BmcsDbContext dbContext, string customerCode, TaxUnit taxUnit = TaxUnit.Invoice,
        string? billingCustomerCode = null)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new CustomerEntity
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = taxUnit == TaxUnit.Line ? (byte)0 : (byte)15,
            TaxUnit = taxUnit,
            RoundingType = RoundingType.Floor,
            BillingCustomerCode = billingCustomerCode ?? customerCode,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static Task InsertBillingAsync(
        BmcsDbContext dbContext, string billingNumber, string customerCode, decimal currentBillingAmount,
        DateOnly? billingDate = null, string closingYearMonth = "202607")
    {
        var now = DateTime.Now;
        dbContext.Billings.Add(new BillingEntity
        {
            BillingNumber = billingNumber,
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "テスト用得意先",
            BillingDate = billingDate ?? new DateOnly(2026, 7, 20),
            ClosingYearMonth = closingYearMonth,
            PreviousBalance = 0m,
            ReceiptAmount = 0m,
            SalesAmount = currentBillingAmount,
            TaxAmount = 0m,
            CurrentBillingAmount = currentBillingAmount,
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

    private static Task InsertSalesLineAsync(
        BmcsDbContext dbContext, string salesSlipNumber, string customerCode, string billingNumber, decimal amount)
    {
        var now = DateTime.Now;
        dbContext.Sales.Add(new SalesEntity
        {
            SalesSlipNumber = salesSlipNumber,
            LineNumber = 1,
            SlipDate = new DateOnly(2026, 7, 1),
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "テスト用得意先",
            SlipType = SlipType.Sales,
            ProductCode = "1001",
            ProductName = "テスト用商品",
            Quantity = 1m,
            UnitPrice = amount,
            Amount = amount,
            CostPrice = 0m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            SlipTaxAmount = null,
            TaxAmount = null,
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Billed,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            BillingNumber = billingNumber,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static async Task CleanupAsync(
        BmcsDbContext dbContext, string customerCode, string? salesSlipNumber = null)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.monthly_closings WHERE customer_code = {customerCode}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.receipt_allocations WHERE customer_code = {customerCode}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.receipts WHERE customer_code = {customerCode}");

        if (salesSlipNumber is not null)
        {
            // sales.billing_number が billing を参照するため、billing より先に削除する。
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.sales WHERE sales_slip_number = {salesSlipNumber}");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.billings WHERE customer_code = {customerCode}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.customers WHERE customer_code = {customerCode}");
    }
}
