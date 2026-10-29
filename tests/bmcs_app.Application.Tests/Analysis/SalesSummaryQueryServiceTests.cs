using bmcs_app.Application.Analysis;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Analysis;

/// <summary>
/// 得意先別・商品別売上・粗利（<see cref="SalesSummaryQueryService"/>）の結合テスト。開発用ライブDBに対して実行する。
/// 実データが存在しない2020年4月だけを使い、後始末でテスト用の売上・得意先を物理削除する。
/// </summary>
public class SalesSummaryQueryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerA = "__TSTSS1";
    private const string CustomerB = "__TSTSS2";
    private const string ProductA = "1001";   // 開発DBの既存商品（FK）
    private const string ProductB = "1002";

    [Fact]
    public async Task 得意先別と商品別に税抜売上と原価から粗利を集計する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var query = scope.ServiceProvider.GetRequiredService<SalesSummaryQueryService>();

        try
        {
            await InsertCustomerAsync(db, CustomerA, TaxUnit.Invoice);
            await InsertCustomerAsync(db, CustomerB, TaxUnit.Line);

            db.Sales.AddRange(
                NewLine("__TSTSS-1", 1, CustomerA, TaxUnit.Invoice, ProductA, new(2020, 4, 1), 2m, 1000m, 300m),
                NewLine("__TSTSS-1", 2, CustomerA, TaxUnit.Invoice, ProductB, new(2020, 4, 1), 1m, 500m, 100m),
                // 内税は税抜で集計（1100 − 100）。
                NewLine("__TSTSS-2", 1, CustomerB, TaxUnit.Line, ProductA, new(2020, 4, 30), 1m, 1100m, 600m, taxAmount: 100m),
                // 取消済み・対象月外は含めない。
                NewLine("__TSTSS-3", 1, CustomerA, TaxUnit.Invoice, ProductA, new(2020, 4, 5), 1m, 9999m, 0m, isDeleted: true),
                NewLine("__TSTSS-4", 1, CustomerA, TaxUnit.Invoice, ProductA, new(2020, 5, 1), 1m, 9999m, 0m));
            await db.SaveChangesAsync();

            var customers = await query.GetByCustomerAsync(2020, 4);
            Assert.Equal([CustomerA, CustomerB], customers.Select(r => r.Code));
            Assert.Equal(1500m, customers[0].SalesAmount);
            Assert.Equal(700m, customers[0].CostAmount);   // 2×300 + 100
            Assert.Equal(800m, customers[0].GrossProfit);
            Assert.Equal("テスト用得意先", customers[0].Name);
            Assert.Equal(1000m, customers[1].SalesAmount);

            var products = await query.GetByProductAsync(2020, 4);
            Assert.Equal([ProductA, ProductB], products.Select(r => r.Code));
            Assert.Equal(2000m, products[0].SalesAmount);  // 1000 + (1100 − 100)
            Assert.Equal(1200m, products[0].CostAmount);   // 2×300 + 600
            Assert.Equal(40.0m, products[0].GrossProfitRate);
            Assert.Equal(500m, products[1].SalesAmount);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.sales WHERE sales_slip_number LIKE '\\_\\_TSTSS-%' ESCAPE '\\'");
            foreach (var code in new[] { CustomerA, CustomerB })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.customers WHERE customer_code = {code}");
            }
        }
    }

    private static SalesEntity NewLine(
        string slipNumber, short lineNumber, string customerCode, TaxUnit taxUnit, string productCode, DateOnly slipDate,
        decimal quantity, decimal amount, decimal costPrice, decimal? taxAmount = null, bool isDeleted = false)
    {
        var now = DateTime.Now;
        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = lineNumber,
            SlipDate = slipDate,
            CustomerCode = customerCode,
            TaxUnit = taxUnit,
            CustomerName = "テスト用得意先",
            SlipType = SlipType.Sales,
            ProductCode = productCode,
            ProductName = "テスト用商品",
            Quantity = quantity,
            UnitPrice = amount / quantity,
            Amount = amount,
            CostPrice = costPrice,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            TaxAmount = taxAmount,
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Unbilled,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            IsDeleted = isDeleted,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    private static async Task InsertCustomerAsync(BmcsDbContext db, string customerCode, TaxUnit taxUnit)
    {
        var now = DateTime.Now;
        db.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = (byte)(taxUnit == TaxUnit.Line ? 0 : 15),
            TaxUnit = taxUnit,
            RoundingType = RoundingType.Floor,
            BillingCustomerCode = customerCode,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }
}
