using bmcs_app.Application.Billing;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Billing;

/// <summary>
/// 明細請求書の印刷データ取得（<see cref="DetailInvoiceService.GetPrintDataAsync"/>）
/// の結合テスト。開発用DBのデータに依存しないよう、各テストが専用のテスト得意先と売上明細行を作成し、
/// <see cref="DetailInvoiceService.IssueAsync"/>／<see cref="DetailInvoiceService.CancelAsync"/>で
/// 明細請求書を用意して、finallyで物理削除する（<see cref="DetailInvoiceServiceTests"/>と同じ方式）。
/// </summary>
public class DetailInvoicePrintDataTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    [Fact]
    public async Task 発行済みの明細請求書は税率別内訳の金額がヘッダーの確定値と一致し税率ラベルを明細行から拝借する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var service = Resolve(scope);

        // 数量25×税込単価108円@軽減8% → 税込2700 = 税抜2500 + 税額200。
        const string customer = "__TSTDPD1";
        const string slip = "__TSTDPD_L1";
        await InsertCustomerAsync(dbContext, customer);
        dbContext.Sales.Add(NewSalesLine(customer, slip, quantity: 25m, unitPrice: 108m));
        await dbContext.SaveChangesAsync();

        try
        {
            var issued = await service.IssueAsync(
                customer, "石山小学校5年1組 佐藤先生", new DateOnly(2025, 9, 10), [(slip, (short)1)]);

            var data = await service.GetPrintDataAsync(issued.DetailInvoiceNumber);

            Assert.Equal("石山小学校5年1組 佐藤先生", data.AddresseeName);
            Assert.Equal(2500.00m, data.TaxExcludedTotal);
            Assert.Equal(200.00m, data.TaxTotal);
            Assert.Equal(2700.00m, data.GrandTotal);

            var bucket = Assert.Single(data.TaxBreakdowns);
            Assert.Equal(TaxCategory.Reduced, bucket.TaxCategory);
            Assert.Equal(8m, bucket.TaxRate);
            Assert.Equal(2500.00m, bucket.TaxableAmount);
            Assert.Equal(200.00m, bucket.TaxAmount);

            var line = Assert.Single(data.Lines);
            Assert.Equal(slip, line.SalesSlipNumber);
        }
        finally
        {
            await CleanupAsync(dbContext, customer, slip);
        }
    }

    [Fact]
    public async Task 取消済みの明細請求書は明細0件でヘッダーの確定金額のみ返り税率は0でフォールバックする()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var service = Resolve(scope);

        // 数量30×税込単価108円@軽減8% → 税込3240 = 税抜3000 + 税額240。
        const string customer = "__TSTDPD2";
        const string slip = "__TSTDPD_L2";
        await InsertCustomerAsync(dbContext, customer);
        dbContext.Sales.Add(NewSalesLine(customer, slip, quantity: 30m, unitPrice: 108m));
        await dbContext.SaveChangesAsync();

        try
        {
            var issued = await service.IssueAsync(customer, "宛名", new DateOnly(2025, 9, 10), [(slip, (short)1)]);
            await service.CancelAsync(issued.DetailInvoiceNumber);

            // 取消で連携行（detail_invoice_sales_line）が物理削除されるため、明細は組み立てられない
            // （`docs/design_document.md` 12-1節の非破壊ヘッダー方式）。
            var data = await service.GetPrintDataAsync(issued.DetailInvoiceNumber);

            Assert.Empty(data.Lines);
            Assert.Equal(3000.00m, data.TaxExcludedTotal);
            Assert.Equal(240.00m, data.TaxTotal);
            Assert.Equal(3240.00m, data.GrandTotal);

            var bucket = Assert.Single(data.TaxBreakdowns);
            Assert.Equal(TaxCategory.Reduced, bucket.TaxCategory);
            Assert.Equal(0m, bucket.TaxRate);
        }
        finally
        {
            await CleanupAsync(dbContext, customer, slip);
        }
    }

    [Fact]
    public async Task 存在しない明細請求書番号は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Resolve(scope);

        await Assert.ThrowsAsync<DetailInvoiceException>(() => service.GetPrintDataAsync("__NOT_EXIST__"));
    }

    private static DetailInvoiceService Resolve(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<DetailInvoiceService>();

    private static async Task InsertCustomerAsync(BmcsDbContext dbContext, string customerCode)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用都度得意先",
            SalesEmployeeCode = "EMP001",
            ClosingDay = 0,
            TaxUnit = TaxUnit.Line,
            RoundingType = RoundingType.Floor,
            BillingCustomerCode = customerCode,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await dbContext.SaveChangesAsync();
    }

    /// <summary>軽減税率8%・内税明細単位（単価は税込）の売上明細行を作る。税抜は税込÷1.08、税額は差額。</summary>
    private static SalesEntity NewSalesLine(string customerCode, string slipNumber, decimal quantity, decimal unitPrice)
    {
        var amount = quantity * unitPrice;
        var now = DateTime.Now;

        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = 1,
            SlipDate = new DateOnly(2025, 9, 1),
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Line,
            CustomerName = "テスト用都度得意先",
            SlipType = SlipType.Sales,
            ProductCode = "PRD001",
            ProductName = "テスト用商品",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = amount,
            CostPrice = 0m,
            TaxCategory = TaxCategory.Reduced,
            TaxRate = 8m,
            SlipTaxAmount = null,
            TaxAmount = amount - amount / 1.08m,
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

    /// <summary>連携行→明細請求書→売上→得意先の順（FK順）に物理削除する。</summary>
    private static async Task CleanupAsync(BmcsDbContext dbContext, string customerCode, string slipNumber)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.detail_invoice_sales_lines WHERE sales_slip_number = {slipNumber}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.detail_invoices WHERE customer_code = {customerCode}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.sales WHERE sales_slip_number = {slipNumber}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.customers WHERE customer_code = {customerCode}");
    }
}
