using bmcs_app.Application.Billing;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BillingEntity = bmcs_app.Domain.Entities.Billing;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Billing;

/// <summary>
/// 請求書の印刷データ取得の結合テスト。開発用ライブDB（172.16.3.171）に対して
/// 実行する。開発用DBのデータに依存しないよう、各テストが専用のテスト得意先・請求データ・売上明細を
/// 自分で作成し、finallyで物理削除する（`closing_day = 18`、他のBillingテストクラスと重複しない値）。
/// 請求データは締め処理を介さず直接登録し、金額は手計算した値を期待値にする。
/// </summary>
public class InvoiceServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const byte TestClosingDay = 18;


    [Fact]
    public async Task 請求単位の請求書は税率別内訳の金額がヘッダーの確定値と一致し税率ラベルを明細行から拝借する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var service = Resolve(scope);

        // 数量10×単価1000@標準10% → 税抜10000/税額1000/請求額11000。
        const string customer = "__TSTINV2";
        const string billingNumber = "__TSTBIL_I1";
        const string slip = "__TSTINV_L1";
        await InsertCustomerAsync(dbContext, customer);
        await InsertBillingAsync(dbContext, billingNumber, customer, TaxUnit.Invoice, BillingStatus.Confirmed,
            salesAmount: 10000m, taxAmount: 1000m, standardTaxable: 10000m, standardTax: 1000m);
        dbContext.Sales.Add(NewSalesLine(customer, slip, 1, new DateOnly(2025, 9, 1), quantity: 10m, unitPrice: 1000m,
            billingNumber: billingNumber));
        await dbContext.SaveChangesAsync();

        try
        {
            await AssertInvoiceBillingAsync(service, billingNumber, slip);
        }
        finally
        {
            await CleanupAsync(dbContext, [slip], [billingNumber], [customer]);
        }
    }

    private static async Task AssertInvoiceBillingAsync(InvoiceService service, string billingNumber, string slip)
    {
        var data = await service.GetByNumberAsync(billingNumber);

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

        // 明細は sales.billing_number がこの請求番号の行から組み立てる。
        var line = Assert.Single(data.Lines);
        Assert.Equal(slip, line.SalesSlipNumber);
    }

    [Fact]
    public async Task 伝票単位の請求書も同様に取得できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var service = Resolve(scope);

        // 数量8×単価1000@標準10%（伝票単位の税額800）→ 税抜8000/税額800/請求額8800。
        const string customer = "__TSTINV3";
        const string billingNumber = "__TSTBIL_S1";
        const string slip = "__TSTINV_L2";
        await InsertCustomerAsync(dbContext, customer, taxUnit: TaxUnit.Slip);
        await InsertBillingAsync(dbContext, billingNumber, customer, TaxUnit.Slip, BillingStatus.Confirmed,
            salesAmount: 8000m, taxAmount: 800m, standardTaxable: 8000m, standardTax: 800m);
        dbContext.Sales.Add(NewSalesLine(customer, slip, 1, new DateOnly(2025, 9, 1), quantity: 8m, unitPrice: 1000m,
            taxUnit: TaxUnit.Slip, billingNumber: billingNumber, slipTaxAmount: 800m));
        await dbContext.SaveChangesAsync();

        try
        {
            var data = await service.GetByNumberAsync(billingNumber);

            Assert.NotNull(data);
            Assert.Equal(TaxUnit.Slip, data!.TaxUnit);
            Assert.Equal(8000.00m, data.SalesAmount);
            Assert.Equal(800.00m, data.TaxTotal);
            Assert.Equal(8800.00m, data.CurrentBillingAmount);
            Assert.Single(data.Lines, l => l.SalesSlipNumber == slip);
        }
        finally
        {
            await CleanupAsync(dbContext, [slip], [billingNumber], [customer]);
        }
    }

    [Fact]
    public async Task 解除済みの請求書は明細0件でヘッダーの確定金額のみ返る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var service = Resolve(scope);

        // 締め解除で sales.billing_number は NULL に戻るため、解除済みの請求番号を指定すると
        // 明細は組み立てられない（`docs/design_document.md` 12-1節と対称の非破壊ヘッダー方式）。
        // ここでは解除後の状態（売上明細が紐付かない解除済みの請求データ）を直接作る。
        const string customer = "__TSTINV4";
        const string billingNumber = "__TSTBIL_R1";
        await InsertCustomerAsync(dbContext, customer);
        await InsertBillingAsync(dbContext, billingNumber, customer, TaxUnit.Invoice, BillingStatus.Released,
            salesAmount: 5000m, taxAmount: 500m, standardTaxable: 5000m, standardTax: 500m);

        try
        {
            var data = await service.GetByNumberAsync(billingNumber);

            Assert.NotNull(data);
            Assert.Empty(data!.Lines);
            Assert.Equal(5000.00m, data.SalesAmount);
            Assert.Equal(500.00m, data.TaxTotal);
            Assert.Equal(5500.00m, data.CurrentBillingAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, [], [billingNumber], [customer]);
        }
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

    [Fact]
    public async Task 指定した請求日の確定済み請求データを得意先コード順で返す()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var closingService = scope.ServiceProvider.GetRequiredService<BillingClosingService>();
        var invoiceService = Resolve(scope);

        const string customerA = "__TSTGBD2";
        const string customerB = "__TSTGBD1";
        await InsertCustomerAsync(dbContext, customerA, salesEmployeeCode: "101");
        await InsertCustomerAsync(dbContext, customerB, salesEmployeeCode: "101");

        var slipA = "__TSTGBD_A";
        var slipB = "__TSTGBD_B";
        var billingDate = new DateOnly(2025, 8, 18);
        dbContext.Sales.Add(NewSalesLine(customerA, slipA, 1, new DateOnly(2025, 8, 1), quantity: 2m, unitPrice: 1000m, productCode: "1001"));
        dbContext.Sales.Add(NewSalesLine(customerB, slipB, 1, new DateOnly(2025, 8, 1), quantity: 3m, unitPrice: 1000m, productCode: "1001"));
        await dbContext.SaveChangesAsync();

        try
        {
            await closingService.ConfirmAsync(TestClosingDay, billingDate);

            var results = await invoiceService.GetByBillingDateAsync(billingDate);
            var testResults = results.Where(r => r.CustomerCode == customerA || r.CustomerCode == customerB).ToList();

            Assert.Equal(2, testResults.Count);
            // 得意先コード順（"__TSTGBD1" < "__TSTGBD2"）に整列されていることを確認する。
            Assert.Equal(customerB, testResults[0].CustomerCode);
            Assert.Equal(customerA, testResults[1].CustomerCode);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.sales WHERE sales_slip_number = {slipA}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.sales WHERE sales_slip_number = {slipB}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.billings WHERE customer_code = {customerA}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.billings WHERE customer_code = {customerB}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.customers WHERE customer_code = {customerA}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.customers WHERE customer_code = {customerB}");
        }
    }

    [Fact]
    public async Task 確定済みが無い請求日は0件を返す()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var invoiceService = Resolve(scope);

        var results = await invoiceService.GetByBillingDateAsync(new DateOnly(1999, 1, 1));

        Assert.Empty(results);
    }

    [Fact]
    public async Task 解除済みの請求データは一覧に含まれない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var closingService = scope.ServiceProvider.GetRequiredService<BillingClosingService>();
        var releaseService = scope.ServiceProvider.GetRequiredService<BillingReleaseService>();
        var invoiceService = Resolve(scope);

        const string customer = "__TSTGBD3";
        await InsertCustomerAsync(dbContext, customer, salesEmployeeCode: "101");

        var slip = "__TSTGBD_C";
        var billingDate = new DateOnly(2025, 8, 19);
        dbContext.Sales.Add(NewSalesLine(customer, slip, 1, new DateOnly(2025, 8, 1), quantity: 1m, unitPrice: 1000m, productCode: "1001"));
        await dbContext.SaveChangesAsync();

        try
        {
            await closingService.ConfirmAsync(TestClosingDay, billingDate);
            await releaseService.ReleaseByBillingDateAsync(billingDate);

            var results = await invoiceService.GetByBillingDateAsync(billingDate);

            Assert.DoesNotContain(results, r => r.CustomerCode == customer);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.sales WHERE sales_slip_number = {slip}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.billings WHERE customer_code = {customer}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.customers WHERE customer_code = {customer}");
        }
    }

    private static InvoiceService Resolve(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<InvoiceService>();

    /// <summary>売上明細→請求データ→得意先の順（FK順）に物理削除する。</summary>
    private static async Task CleanupAsync(
        BmcsDbContext dbContext, string[] slips, string[] billingNumbers, string[] customers)
    {
        foreach (var slip in slips)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.sales WHERE sales_slip_number = {slip}");
        }

        foreach (var billingNumber in billingNumbers)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.billings WHERE billing_number = {billingNumber}");
        }

        foreach (var customer in customers)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.customers WHERE customer_code = {customer}");
        }
    }

    /// <summary>標準税率のみの請求データ（ヘッダー）を登録する。繰越・入金は0。</summary>
    private static async Task InsertBillingAsync(
        BmcsDbContext dbContext, string billingNumber, string customerCode, TaxUnit taxUnit, BillingStatus status,
        decimal salesAmount, decimal taxAmount, decimal standardTaxable, decimal standardTax)
    {
        var now = DateTime.Now;
        dbContext.Billings.Add(new BillingEntity
        {
            BillingNumber = billingNumber,
            CustomerCode = customerCode,
            TaxUnit = taxUnit,
            CustomerName = "テスト用得意先",
            BillingDate = new DateOnly(2025, 9, 18),
            ClosingYearMonth = "202509",
            PreviousBalance = 0m,
            ReceiptAmount = 0m,
            SalesAmount = salesAmount,
            TaxAmount = taxAmount,
            CurrentBillingAmount = salesAmount + taxAmount,
            StandardRateTaxableAmount = standardTaxable,
            StandardRateTaxAmount = standardTax,
            ReducedRateTaxableAmount = 0m,
            ReducedRateTaxAmount = 0m,
            TaxExemptAmount = 0m,
            BillingStatus = status,
            ConfirmedAt = now,
            ConfirmedBy = "TEST",
            ReleasedAt = status == BillingStatus.Released ? now : null,
            ReleasedBy = status == BillingStatus.Released ? "TEST" : null,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task InsertCustomerAsync(
        BmcsDbContext dbContext, string customerCode, string? billingCustomerCode = null,
        string salesEmployeeCode = "EMP001", TaxUnit taxUnit = TaxUnit.Invoice)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            SalesEmployeeCode = salesEmployeeCode,
            ClosingDay = TestClosingDay,
            TaxUnit = taxUnit,
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
        decimal unitPrice, string productCode = "PRD001", TaxUnit taxUnit = TaxUnit.Invoice,
        string? billingNumber = null, decimal? slipTaxAmount = null)
    {
        var amount = ConsumptionTaxCalculator.CalculateLineAmount(quantity, unitPrice, RoundingType.Floor);
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
            UnitPrice = unitPrice,
            Amount = amount,
            CostPrice = 700m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            SlipTaxAmount = slipTaxAmount,
            DeliveryNoteIssueCount = 0,
            BillingStatus = billingNumber is null ? BillingLinkStatus.Unbilled : BillingLinkStatus.Billed,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            BillingNumber = billingNumber,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }
}
