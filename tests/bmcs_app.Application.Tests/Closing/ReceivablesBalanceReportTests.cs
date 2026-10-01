using bmcs_app.Application.Closing;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Closing;

/// <summary>
/// 売掛金残高一覧表の印刷データ（<see cref="MonthlyClosingQueryService.GetReceivablesBalanceReportAsync"/>）の
/// 結合テスト。開発用ライブDB（172.16.3.171）に対して実行する。
/// <see cref="MonthlyClosingServiceTests"/>と同じく、実データが存在しない2020年1月だけを使い、
/// 後始末でその月の <c>monthly_closings</c> を全件物理削除する。
/// </summary>
public class ReceivablesBalanceReportTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerSolo = "__TSTRBR1";
    private const string CustomerRoot = "__TSTRBR2";
    private const string CustomerChild = "__TSTRBR3";
    private const string CustomerReleased = "__TSTRBR4";

    private static readonly DateOnly Jan = new(2020, 1, 31);

    [Fact]
    public async Task 確定済みだけをコード順に返し請求集約元は合計から除かれる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var query = scope.ServiceProvider.GetRequiredService<MonthlyClosingQueryService>();

        try
        {
            // 請求集約元は FK の都合で請求集約先より後に登録する。
            await InsertCustomerAsync(db, CustomerSolo, CustomerSolo);
            await InsertCustomerAsync(db, CustomerRoot, CustomerRoot);
            await InsertCustomerAsync(db, CustomerChild, CustomerRoot);
            await InsertCustomerAsync(db, CustomerReleased, CustomerReleased);

            db.MonthlyClosings.AddRange(
                NewRow(CustomerRoot, 100m, 50m, 400m, 40m, 490m),
                // 請求集約元は売上だけを持ち、残高系は0（docs/design_document.md 29-1）。
                NewRow(CustomerChild, 0m, 0m, 200m, 0m, 0m),
                NewRow(CustomerSolo, 10m, 5m, 30m, 3m, 38m),
                NewRow(CustomerReleased, 1m, 1m, 1m, 1m, 2m, ClosingStatus.Released));
            await db.SaveChangesAsync();

            var data = await query.GetReceivablesBalanceReportAsync(2020, 1);

            // 解除済みは含まず、得意先コード順。
            Assert.Equal([CustomerSolo, CustomerRoot, CustomerChild], data.Rows.Select(r => r.CustomerCode));
            Assert.Equal([false, false, true], data.Rows.Select(r => r.IsBillingChild));
            Assert.True(data.HasBillingChild);

            // 合計は請求集約元（売上200）を除く。
            Assert.Equal(110m, data.TotalPreviousBalance);
            Assert.Equal(55m, data.TotalReceiptAmount);
            Assert.Equal(430m, data.TotalSalesAmount);
            Assert.Equal(43m, data.TotalTaxAmount);
            Assert.Equal(528m, data.TotalClosingBalance);

            Assert.Equal(2020, data.Year);
            Assert.Equal(1, data.Month);
            Assert.Equal(new DateOnly(2020, 1, 1), data.PeriodFrom);
            Assert.Equal(Jan, data.PeriodTo);
        }
        finally
        {
            await CleanupAsync(db);
        }
    }

    [Fact]
    public async Task 確定済みの行が無い年月は空を返す()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<MonthlyClosingQueryService>();

        var data = await query.GetReceivablesBalanceReportAsync(2020, 1);

        Assert.Empty(data.Rows);
        Assert.False(data.HasBillingChild);
        Assert.Equal(0m, data.TotalClosingBalance);
    }

    private static MonthlyClosing NewRow(
        string customerCode, decimal previous, decimal receipt, decimal sales, decimal tax, decimal closing,
        ClosingStatus status = ClosingStatus.Confirmed)
    {
        var now = DateTime.Now;
        return new MonthlyClosing
        {
            ClosingDate = Jan,
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "テスト用得意先",
            PreviousBalance = previous,
            ReceiptAmount = receipt,
            SalesAmount = sales,
            TaxAmount = tax,
            ClosingBalance = closing,
            StandardRateTaxableAmount = sales,
            StandardRateTaxAmount = tax,
            ReducedRateTaxableAmount = 0m,
            ReducedRateTaxAmount = 0m,
            TaxExemptAmount = 0m,
            ClosingStatus = status,
            ConfirmedAt = now,
            ConfirmedBy = "TEST",
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    private static async Task InsertCustomerAsync(BmcsDbContext db, string customerCode, string billingCustomerCode)
    {
        var now = DateTime.Now;
        db.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            SalesEmployeeCode = null,
            ClosingDay = 15,
            TaxUnit = TaxUnit.Invoice,
            RoundingType = RoundingType.Floor,
            BillingCustomerCode = billingCustomerCode,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>1月分の <c>monthly_closings</c> を全件消し、テスト得意先を子（請求集約元）→親の順に物理削除する。</summary>
    private static async Task CleanupAsync(BmcsDbContext db)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.monthly_closings WHERE closing_date = {Jan}");

        foreach (var customerCode in new[] { CustomerChild, CustomerRoot, CustomerSolo, CustomerReleased })
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.customers WHERE customer_code = {customerCode}");
        }
    }
}
