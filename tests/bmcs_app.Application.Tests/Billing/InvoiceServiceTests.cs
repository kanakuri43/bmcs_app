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
/// 請求書の印刷データ取得（TODO.md 10-5、12-D）の結合テスト。開発用ライブDB（172.16.3.171）に対して
/// 実行する。既存のテストは読み取り専用（保存しない）のため専用のテスト得意先を新設せず、既存の
/// seedデータ（`scripts/seed_dev_data.sql`）に対する回帰検知テストとして実装している
/// （`CustomerLedgerQueryServiceTests`と同じ方針）。親子請求（請求集約）のテストのみ、seedデータに
/// 集約シナリオが無いため`BillingClosingServiceTests`と同じ「専用のテスト得意先で締めた後、
/// finallyで物理削除する」方式を使う（`closing_day = 18`、他のBillingテストクラスと重複しない値）。
/// </summary>
public class InvoiceServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const byte TestClosingDay = 18;


    [Fact]
    public async Task 請求単位の請求書は税率別内訳の金額がヘッダーの確定値と一致し税率ラベルを明細行から拝借する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Resolve(scope);

        var data = await service.GetByNumberAsync("BIL_INV001");

        Assert.NotNull(data);
        Assert.Equal(TaxUnit.Invoice, data!.TaxUnit);
        Assert.Equal(10000.00m, data.SalesAmount);
        Assert.Equal(1000.00m, data.TaxTotal);
        Assert.Equal(11000.00m, data.CurrentBillingAmount);

        var bucket = Assert.Single(data.TaxBreakdowns);
        Assert.Equal(TaxCategory.Standard, bucket.TaxCategory);
        Assert.Equal(10m, bucket.TaxRate);
        Assert.Equal(10000.00m, bucket.TaxableAmount);
        Assert.Equal(1000.00m, bucket.TaxAmount);

        // 明細は sales.billing_number = 'BIL_INV001' の行（SALINV002）から組み立てる。
        var line = Assert.Single(data.Lines);
        Assert.Equal("SALINV002", line.SalesSlipNumber);
    }

    [Fact]
    public async Task 伝票単位の請求書も同様に取得できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Resolve(scope);

        var data = await service.GetByNumberAsync("BIL_SLP001");

        Assert.NotNull(data);
        Assert.Equal(TaxUnit.Slip, data!.TaxUnit);
        Assert.Equal(8000.00m, data.SalesAmount);
        Assert.Equal(800.00m, data.TaxTotal);
        Assert.Equal(8800.00m, data.CurrentBillingAmount);
        Assert.Single(data.Lines, l => l.SalesSlipNumber == "SALSLP002");
    }

    [Fact]
    public async Task 解除済みの請求書は明細0件でヘッダーの確定金額のみ返る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Resolve(scope);

        // 締め解除で sales.billing_number は NULL に戻るため、解除済みの請求番号を指定すると
        // 明細は組み立てられない（`docs/design_document.md` 12-1節と対称の非破壊ヘッダー方式）。
        var data = await service.GetByNumberAsync("BIL_INV002");

        Assert.NotNull(data);
        Assert.Empty(data!.Lines);
        Assert.Equal(5000.00m, data.SalesAmount);
        Assert.Equal(500.00m, data.TaxTotal);
        Assert.Equal(5500.00m, data.CurrentBillingAmount);
    }

    [Fact]
    public async Task 存在しない請求番号はnullを返す()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = Resolve(scope);

        var data = await service.GetByNumberAsync("__NOT_EXIST__");

        Assert.Null(data);
    }

    [Fact]
    public async Task 請求集約先の請求書は請求集約元の明細も得意先コード順に含む()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var closingService = scope.ServiceProvider.GetRequiredService<BillingClosingService>();
        var invoiceService = Resolve(scope);

        const string root = "__TSTINV1";
        const string child = "__TSTINV1C";
        await InsertCustomerAsync(dbContext, root, salesEmployeeCode: "101");
        await InsertCustomerAsync(dbContext, child, billingCustomerCode: root, salesEmployeeCode: "101");

        var slipRoot = "__TSTINV_AGGR";
        var slipChild = "__TSTINV_AGGC";
        dbContext.Sales.Add(NewSalesLine(root, slipRoot, 1, new DateOnly(2025, 7, 1), quantity: 5m, unitPrice: 1000m, productCode: "1001"));
        dbContext.Sales.Add(NewSalesLine(child, slipChild, 1, new DateOnly(2025, 7, 1), quantity: 3m, unitPrice: 1000m, productCode: "1001"));
        await dbContext.SaveChangesAsync();

        try
        {
            var confirmed = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 18));
            var billingNumber = Assert.Single(confirmed, r => r.CustomerCode == root).BillingNumber!;

            var data = await invoiceService.GetByNumberAsync(billingNumber);

            Assert.NotNull(data);
            Assert.Equal(root, data!.CustomerCode);
            Assert.Equal(2, data.Lines.Count);
            // 得意先コード順（"__TSTINV1" < "__TSTINV1C"）に整列されていることを確認する
            // （InvoiceReportRowBuilderが見出し行・小計行を組み立てるための前提）。
            Assert.Equal(root, data.Lines[0].CustomerCode);
            Assert.Equal(slipRoot, data.Lines[0].SalesSlipNumber);
            Assert.Equal(child, data.Lines[1].CustomerCode);
            Assert.Equal(slipChild, data.Lines[1].SalesSlipNumber);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.sales WHERE sales_slip_number = {slipRoot}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.sales WHERE sales_slip_number = {slipChild}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.billings WHERE customer_code = {root}");
            // 自己参照FK（billing_customer_code）のため請求集約元(child)を請求集約先(root)より先に削除する。
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.customers WHERE customer_code = {child}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.customers WHERE customer_code = {root}");
        }
    }

    private static InvoiceService Resolve(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<InvoiceService>();

    private static async Task InsertCustomerAsync(
        BmcsDbContext dbContext, string customerCode, string? billingCustomerCode = null,
        string salesEmployeeCode = "EMP001")
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            SalesEmployeeCode = salesEmployeeCode,
            ClosingDay = TestClosingDay,
            TaxUnit = TaxUnit.Invoice,
            RoundingType = RoundingType.Floor,
            BillingCustomerCode = billingCustomerCode ?? customerCode,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await dbContext.SaveChangesAsync();
    }

    private static SalesEntity NewSalesLine(
        string customerCode, string slipNumber, short lineNumber, DateOnly slipDate, decimal quantity,
        decimal unitPrice, string productCode = "PRD001")
    {
        var amount = ConsumptionTaxCalculator.CalculateLineAmount(quantity, unitPrice, RoundingType.Floor);
        var now = DateTime.Now;

        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = lineNumber,
            SlipDate = slipDate,
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "テスト用得意先",
            SlipType = SlipType.Sales,
            ProductCode = productCode,
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
}
