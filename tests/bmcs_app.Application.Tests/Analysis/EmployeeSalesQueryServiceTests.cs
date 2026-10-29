using bmcs_app.Application.Analysis;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Analysis;

/// <summary>
/// 担当者別売上・粗利（<see cref="EmployeeSalesQueryService"/>）の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する。
/// 実データが存在しない2020年2月だけを使い、後始末でテスト用の売上・得意先を物理削除する。
/// </summary>
public class EmployeeSalesQueryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerWithRep = "__TSTES1";   // 営業担当 EMP001
    private const string CustomerNoRep = "__TSTES2";     // 営業担当なし
    private const string CustomerLine = "__TSTES3";      // 内税明細単位、営業担当なし
    private const string Employee = "EMP001";

    [Fact]
    public async Task 伝票の担当者_無ければ得意先の営業担当で集計し税抜売上と原価から粗利を出す()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var query = scope.ServiceProvider.GetRequiredService<EmployeeSalesQueryService>();

        try
        {
            await InsertCustomerAsync(db, CustomerWithRep, Employee, 15, TaxUnit.Invoice);
            await InsertCustomerAsync(db, CustomerNoRep, null, 15, TaxUnit.Invoice);
            await InsertCustomerAsync(db, CustomerLine, null, 0, TaxUnit.Line);

            db.Sales.AddRange(
                // 伝票の担当者 EMP001（2行）。
                NewLine("__TSTES-1", 1, CustomerNoRep, TaxUnit.Invoice, new(2020, 2, 1), Employee, 2m, 1000m, 300m),
                NewLine("__TSTES-1", 2, CustomerNoRep, TaxUnit.Invoice, new(2020, 2, 1), Employee, 1m, 500m, 100m),
                // 担当者未設定 → 得意先の営業担当 EMP001。
                NewLine("__TSTES-2", 1, CustomerWithRep, TaxUnit.Invoice, new(2020, 2, 29), null, 1m, 400m, 250m),
                // 担当者未設定・営業担当なし → 担当者なし。内税は税抜で集計（1100 − 100）。
                NewLine("__TSTES-3", 1, CustomerLine, TaxUnit.Line, new(2020, 2, 10), null, 1m, 1100m, 600m, taxAmount: 100m),
                // 取消済み・対象月外は含めない。
                NewLine("__TSTES-4", 1, CustomerNoRep, TaxUnit.Invoice, new(2020, 2, 5), Employee, 1m, 9999m, 0m, isDeleted: true),
                NewLine("__TSTES-5", 1, CustomerNoRep, TaxUnit.Invoice, new(2020, 3, 1), Employee, 1m, 9999m, 0m));
            await db.SaveChangesAsync();

            var rows = await query.GetByMonthAsync(2020, 2);

            Assert.Equal([Employee, null], rows.Select(r => r.EmployeeCode));

            var employee = rows[0];
            Assert.Equal(1900m, employee.SalesAmount);   // 1000 + 500 + 400
            Assert.Equal(950m, employee.CostAmount);     // 2×300 + 100 + 250
            Assert.Equal(950m, employee.GrossProfit);
            Assert.Equal(50.0m, employee.GrossProfitRate);

            var none = rows[1];
            Assert.Equal("（担当者なし）", none.EmployeeName);
            Assert.Equal(1000m, none.SalesAmount);
            Assert.Equal(400m, none.GrossProfit);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.sales WHERE sales_slip_number LIKE '\\_\\_TSTES-%' ESCAPE '\\'");
            foreach (var code in new[] { CustomerWithRep, CustomerNoRep, CustomerLine })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.customers WHERE customer_code = {code}");
            }
        }
    }

    private static SalesEntity NewLine(
        string slipNumber, short lineNumber, string customerCode, TaxUnit taxUnit, DateOnly slipDate, string? employeeCode,
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
            ProductCode = "PRD001",
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
            EmployeeCode = employeeCode,
            IsDeleted = isDeleted,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    private static async Task InsertCustomerAsync(
        BmcsDbContext db, string customerCode, string? salesEmployeeCode, byte closingDay, TaxUnit taxUnit)
    {
        var now = DateTime.Now;
        db.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            SalesEmployeeCode = salesEmployeeCode,
            ClosingDay = closingDay,
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
