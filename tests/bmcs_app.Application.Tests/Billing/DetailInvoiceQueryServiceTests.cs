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
/// 明細請求書検索（伝票検索モーダル用、docs/product-spec.md UI/UX節「ジャーナル系画面の
/// 伝票No入力欄の挙動」の Space キー検索）の結合テスト。開発用ライブDB（172.16.3.171）に対して
/// 実行する（<see cref="DetailInvoiceServiceTests"/> と同じ「専用のテスト得意先で発行した後、
/// finallyで物理削除する」方式を採る）。
/// </summary>
public class DetailInvoiceQueryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerCode = "__TSTDIVQ1";

    [Fact]
    public async Task 宛名でも検索できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, invoiceService, queryService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTDIVQ_SLIP1";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slip, 1, new DateOnly(2025, 7, 1), 1m, 110m));
        await dbContext.SaveChangesAsync();

        string? invoiceNumber = null;
        try
        {
            var issued = await invoiceService.IssueAsync(
                CustomerCode, "石山小学校5年2組 山田先生", new DateOnly(2025, 7, 10), [(slip, (short)1)]);
            invoiceNumber = issued.DetailInvoiceNumber;

            var byNumber = await queryService.SearchAsync(issued.DetailInvoiceNumber);
            Assert.Single(byNumber, h => h.DetailInvoiceNumber == issued.DetailInvoiceNumber);

            var byAddressee = await queryService.SearchAsync("山田先生");
            Assert.Contains(byAddressee, h => h.DetailInvoiceNumber == issued.DetailInvoiceNumber);

            var byCustomerCode = await queryService.SearchAsync(CustomerCode);
            Assert.Contains(byCustomerCode, h => h.DetailInvoiceNumber == issued.DetailInvoiceNumber);

            var byUnrelatedKeyword = await queryService.SearchAsync("__NOMATCH_KEYWORD__");
            Assert.DoesNotContain(byUnrelatedKeyword, h => h.DetailInvoiceNumber == issued.DetailInvoiceNumber);
        }
        finally
        {
            await CleanupAsync(dbContext, CustomerCode, [slip]);
        }
    }

    [Fact]
    public async Task 取消済みも検索結果に含まれる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, invoiceService, queryService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTDIVQ_SLIP2";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slip, 1, new DateOnly(2025, 7, 1), 1m, 110m));
        await dbContext.SaveChangesAsync();

        try
        {
            var issued = await invoiceService.IssueAsync(CustomerCode, "宛名", new DateOnly(2025, 7, 10), [(slip, (short)1)]);
            await invoiceService.CancelAsync(issued.DetailInvoiceNumber);

            var results = await queryService.SearchAsync(issued.DetailInvoiceNumber);

            var hit = Assert.Single(results, h => h.DetailInvoiceNumber == issued.DetailInvoiceNumber);
            Assert.Equal(DetailInvoiceStatus.Cancelled, hit.InvoiceStatus);
        }
        finally
        {
            await CleanupAsync(dbContext, CustomerCode, [slip]);
        }
    }

    private static (BmcsDbContext DbContext, DetailInvoiceService InvoiceService, DetailInvoiceQueryService QueryService) Resolve(
        AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<DetailInvoiceService>(),
        scope.ServiceProvider.GetRequiredService<DetailInvoiceQueryService>());

    private static async Task InsertCustomerAsync(BmcsDbContext dbContext, string customerCode)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用都度得意先（検索）",
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

    private static SalesEntity NewSalesLine(
        string customerCode, string slipNumber, short lineNumber, DateOnly slipDate, decimal quantity, decimal unitPrice)
    {
        const RoundingType roundingType = RoundingType.Floor;
        const TaxCategory taxCategory = TaxCategory.Standard;
        const decimal taxRate = 10m;

        var amount = ConsumptionTaxCalculator.CalculateLineAmount(quantity, unitPrice, roundingType);
        var taxAmount = ConsumptionTaxCalculator.CalculateInternalTaxAmount(new TaxLine(taxCategory, taxRate, amount), roundingType);
        var now = DateTime.Now;

        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = lineNumber,
            SlipDate = slipDate,
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Line,
            CustomerName = "テスト用都度得意先（検索）",
            SlipType = SlipType.Sales,
            ProductCode = "PRD001",
            ProductName = "テスト用商品",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = amount,
            CostPrice = 0m,
            TaxCategory = taxCategory,
            TaxRate = taxRate,
            SlipTaxAmount = null,
            TaxAmount = taxAmount,
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Unbilled,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            BillingNumber = null,
            IsDeleted = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    private static async Task CleanupAsync(BmcsDbContext dbContext, string customerCode, IReadOnlyList<string> salesSlipNumbers)
    {
        foreach (var slipNumber in salesSlipNumbers)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.detail_invoice_sales_lines WHERE sales_slip_number = {slipNumber}");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.detail_invoices WHERE customer_code = {customerCode}");

        foreach (var slipNumber in salesSlipNumbers)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.sales WHERE sales_slip_number = {slipNumber}");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.customers WHERE customer_code = {customerCode}");
    }
}
