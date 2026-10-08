using bmcs_app.Application.Billing;
using bmcs_app.Application.Closing;
using bmcs_app.Application.Ledger;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReceiptEntity = bmcs_app.Domain.Entities.Receipt;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Closing;

/// <summary>
/// 月次締め処理の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する。
/// 完了条件「集計値が元帳の残高と一致する」の実証。
/// </summary>
/// <remarks>
/// <see cref="MonthlyClosingService.ConfirmAsync"/> は全得意先を対象にするため、seed・開発データに
/// 影響しないよう、実データが存在しない2020年の月（1月・2月）だけを締める。後始末で
/// 該当月の <c>monthly_closings</c> を全件物理削除する（この月の行はテストでしか作られない）。
/// サービスが独自にトランザクションを開くため、ロールバック方式ではなく
/// 「専用のテスト得意先で確定 → finallyで物理削除」方式を採る（BillingClosingServiceTests と同じ）。
/// </remarks>
public class MonthlyClosingServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerInvoice = "__TSTMCL1";
    private const string CustomerSlip = "__TSTMCL2";
    private const string CustomerLine = "__TSTMCL3";
    private const string CustomerChild = "__TSTMCL4";
    private const byte BillingClosingDay = 15;

    private static readonly DateOnly Jan = new(2020, 1, 31);
    private static readonly DateOnly Feb = new(2020, 2, 29);

    [Fact]
    public async Task 請求単位の得意先の当月残高は元帳の月末残高と一致する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, ledger) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice, RoundingType.Floor);
        var slip = "__TSTMCL_S01";
        dbContext.Sales.AddRange(
            NewSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, new DateOnly(2020, 1, 10),
                TaxCategory.Standard, 10m, 5m, 1000m, RoundingType.Floor),
            NewSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 2, new DateOnly(2020, 1, 10),
                TaxCategory.Reduced, 8m, 2m, 500m, RoundingType.Floor));
        dbContext.Receipts.Add(NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, "__TSTMCL_R01", 1, new DateOnly(2020, 1, 20), 1000m));
        await dbContext.SaveChangesAsync();

        try
        {
            var results = await service.ConfirmAsync(2020, 1);

            var target = Assert.Single(results, r => r.CustomerCode == CustomerInvoice);
            Assert.Null(target.SkipReason);
            // 売上6000、仮計算税 500+80=580、入金1000 → 5580。
            Assert.Equal(0m, target.PreviousBalance);
            Assert.Equal(6000m, target.SalesAmount);
            Assert.Equal(580m, target.TaxAmount);
            Assert.Equal(1000m, target.ReceiptAmount);
            Assert.Equal(5580m, target.ClosingBalance);

            var expected = await ledger.GetAsync(CustomerInvoice, new DateOnly(2020, 1, 1), Jan);
            Assert.Equal(expected!.ClosingBalance, target.ClosingBalance);

            var row = await dbContext.MonthlyClosings.AsNoTracking()
                .SingleAsync(m => m.ClosingDate == Jan && m.CustomerCode == CustomerInvoice);
            Assert.Equal(ClosingStatus.Confirmed, row.ClosingStatus);
            Assert.Equal(6000m, row.StandardRateTaxableAmount + row.ReducedRateTaxableAmount);
            Assert.Equal(500m, row.StandardRateTaxAmount);
            Assert.Equal(80m, row.ReducedRateTaxAmount);
            Assert.Equal(row.PreviousBalance + row.SalesAmount + row.TaxAmount - row.ReceiptAmount, row.ClosingBalance);
        }
        finally
        {
            await CleanupAsync(dbContext, [slip], ["__TSTMCL_R01"], CustomerInvoice);
        }
    }

    [Fact]
    public async Task 請求締めで仮計算税が確定しても前月残高は連続し消費税で吸収される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, ledger) = Resolve(scope);
        var billingClosing = scope.ServiceProvider.GetRequiredService<BillingClosingService>();

        await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice, RoundingType.Floor);
        var slip = "__TSTMCL_S02";
        dbContext.Sales.Add(NewSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, new DateOnly(2020, 1, 20),
            TaxCategory.Standard, 10m, 5m, 1000m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            // 1月: 1/20の売上は未締めのため税500は仮計算。当月残高 5500。
            await service.ConfirmAsync(2020, 1);

            // 2/15に請求締め（売上5000＋税500=5500を請求）→ 仮計算税が確定値へ置き換わる。
            await billingClosing.ConfirmAsync(BillingClosingDay, new DateOnly(2020, 2, 15));
            dbContext.Receipts.Add(NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, "__TSTMCL_R02", 1, new DateOnly(2020, 2, 20), 5500m));
            await dbContext.SaveChangesAsync();

            var results = await service.ConfirmAsync(2020, 2);

            var jan = await dbContext.MonthlyClosings.AsNoTracking()
                .SingleAsync(m => m.ClosingDate == Jan && m.CustomerCode == CustomerInvoice);
            Assert.Equal(5500m, jan.ClosingBalance);

            var target = Assert.Single(results, r => r.CustomerCode == CustomerInvoice);
            Assert.Null(target.SkipReason);
            // 前月残高は1月の当月残高（元帳の月初残高5000ではない）。
            Assert.Equal(5500m, target.PreviousBalance);
            Assert.Equal(0m, target.SalesAmount);
            Assert.Equal(5500m, target.ReceiptAmount);
            Assert.Equal(0m, target.ClosingBalance);
            Assert.Equal(0m, target.TaxAmount);

            var expected = await ledger.GetAsync(CustomerInvoice, new DateOnly(2020, 2, 1), Feb);
            Assert.Equal(expected!.ClosingBalance, target.ClosingBalance);
            Assert.Equal(
                target.PreviousBalance + target.SalesAmount + target.TaxAmount - target.ReceiptAmount,
                target.ClosingBalance);
        }
        finally
        {
            await CleanupAsync(dbContext, [slip], ["__TSTMCL_R02"], CustomerInvoice);
        }
    }

    [Fact]
    public async Task 伝票単位の得意先は伝票ごとの税額を税率別内訳に保存する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, ledger) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerSlip, TaxUnit.Slip, RoundingType.RoundHalfUp);
        var slip1 = "__TSTMCL_S03";
        var slip2 = "__TSTMCL_S04";
        var lines = new List<SalesEntity>
        {
            // 3702×10%=370.2→370
            NewSalesLine(CustomerSlip, TaxUnit.Slip, slip1, 1, new DateOnly(2020, 1, 5),
                TaxCategory.Standard, 10m, 3m, 1234m, RoundingType.RoundHalfUp),
            // 500×8%=40
            NewSalesLine(CustomerSlip, TaxUnit.Slip, slip2, 1, new DateOnly(2020, 1, 6),
                TaxCategory.Reduced, 8m, 1m, 500m, RoundingType.RoundHalfUp),
        };
        foreach (var group in lines.GroupBy(l => l.SalesSlipNumber))
        {
            var tax = ConsumptionTaxCalculator.CalculateSlipTaxAmount(
                group.Select(l => new TaxLine(l.TaxCategory, l.TaxRate, l.Amount)), RoundingType.RoundHalfUp);
            foreach (var line in group)
            {
                line.SlipTaxAmount = tax;
            }
        }

        dbContext.Sales.AddRange(lines);
        await dbContext.SaveChangesAsync();

        try
        {
            var results = await service.ConfirmAsync(2020, 1);

            var target = Assert.Single(results, r => r.CustomerCode == CustomerSlip);
            Assert.Equal(4202m, target.SalesAmount);
            Assert.Equal(410m, target.TaxAmount);
            Assert.Equal(4612m, target.ClosingBalance);

            var expected = await ledger.GetAsync(CustomerSlip, new DateOnly(2020, 1, 1), Jan);
            Assert.Equal(expected!.ClosingBalance, target.ClosingBalance);

            var row = await dbContext.MonthlyClosings.AsNoTracking()
                .SingleAsync(m => m.ClosingDate == Jan && m.CustomerCode == CustomerSlip);
            Assert.Equal(370m, row.StandardRateTaxAmount);
            Assert.Equal(40m, row.ReducedRateTaxAmount);
            Assert.Equal(3702m, row.StandardRateTaxableAmount);
            Assert.Equal(500m, row.ReducedRateTaxableAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, [slip1, slip2], [], CustomerSlip);
        }
    }

    [Fact]
    public async Task 内税の都度得意先は売上を税抜額で消費税を内税額で保存し元帳と一致する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, ledger) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerLine, TaxUnit.Line, RoundingType.Floor, closingDay: 0);
        var slip = "__TSTMCL_S05";
        // 税込1100（標準10%）→ 内税額100、税抜1000。
        var line = NewSalesLine(CustomerLine, TaxUnit.Line, slip, 1, new DateOnly(2020, 1, 8),
            TaxCategory.Standard, 10m, 1m, 1100m, RoundingType.Floor);
        line.TaxAmount = ConsumptionTaxCalculator.CalculateInternalTaxAmount(
            new TaxLine(line.TaxCategory, line.TaxRate, line.Amount), RoundingType.Floor);
        dbContext.Sales.Add(line);
        await dbContext.SaveChangesAsync();

        try
        {
            var results = await service.ConfirmAsync(2020, 1);

            var target = Assert.Single(results, r => r.CustomerCode == CustomerLine);
            Assert.Equal(1000m, target.SalesAmount);
            Assert.Equal(100m, target.TaxAmount);
            Assert.Equal(1100m, target.ClosingBalance);

            var expected = await ledger.GetAsync(CustomerLine, new DateOnly(2020, 1, 1), Jan);
            Assert.Equal(expected!.ClosingBalance, target.ClosingBalance);
        }
        finally
        {
            await CleanupAsync(dbContext, [slip], [], CustomerLine);
        }
    }

    [Fact]
    public async Task 請求集約先はグループ合算の残高を請求集約元は自社売上のみを保存する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, ledger) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice, RoundingType.Floor);
        await InsertCustomerAsync(dbContext, CustomerChild, TaxUnit.Invoice, RoundingType.Floor,
            billingCustomerCode: CustomerInvoice);
        var rootSlip = "__TSTMCL_S06";
        var childSlip = "__TSTMCL_S07";
        dbContext.Sales.AddRange(
            NewSalesLine(CustomerInvoice, TaxUnit.Invoice, rootSlip, 1, new DateOnly(2020, 1, 10),
                TaxCategory.Standard, 10m, 3m, 1000m, RoundingType.Floor),
            NewSalesLine(CustomerChild, TaxUnit.Invoice, childSlip, 1, new DateOnly(2020, 1, 11),
                TaxCategory.Standard, 10m, 2m, 1000m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            var results = await service.ConfirmAsync(2020, 1);

            var root = Assert.Single(results, r => r.CustomerCode == CustomerInvoice);
            Assert.Equal(5000m, root.SalesAmount);
            Assert.Equal(500m, root.TaxAmount);
            Assert.Equal(5500m, root.ClosingBalance);
            var expected = await ledger.GetAsync(CustomerInvoice, new DateOnly(2020, 1, 1), Jan);
            Assert.Equal(expected!.ClosingBalance, root.ClosingBalance);

            var child = Assert.Single(results, r => r.CustomerCode == CustomerChild);
            Assert.Null(child.SkipReason);
            Assert.Equal(2000m, child.SalesAmount);
            Assert.Equal(0m, child.TaxAmount);
            Assert.Equal(0m, child.ReceiptAmount);
            Assert.Equal(0m, child.ClosingBalance);

            var childRow = await dbContext.MonthlyClosings.AsNoTracking()
                .SingleAsync(m => m.ClosingDate == Jan && m.CustomerCode == CustomerChild);
            Assert.Equal(2000m, childRow.StandardRateTaxableAmount);
            Assert.Equal(0m, childRow.StandardRateTaxAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, [rootSlip, childSlip], [], CustomerChild, CustomerInvoice);
        }
    }

    [Fact]
    public async Task 確定済みや後の月が確定済みや前月解除済みは飛ばし当月が解除済みなら再確定する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice, RoundingType.Floor);
        var slip = "__TSTMCL_S08";
        dbContext.Sales.Add(NewSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, new DateOnly(2020, 1, 10),
            TaxCategory.Standard, 10m, 1m, 1000m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            await service.ConfirmAsync(2020, 1);

            // 二重確定は飛ばされ、行は1件のまま。
            var second = await service.ConfirmAsync(2020, 1);
            Assert.Contains("確定済み", Assert.Single(second, r => r.CustomerCode == CustomerInvoice).SkipReason);

            // 2月を確定してから1月をやり直そうとしても、後の月が確定済みのため飛ばされる。
            await service.ConfirmAsync(2020, 2);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.monthly_closings SET closing_status = 2, released_at = SYSDATETIME(), released_by = 'TEST' WHERE closing_date = {Jan} AND customer_code = {CustomerInvoice}");
            var reconfirmJan = await service.ConfirmAsync(2020, 1);
            Assert.Contains("後の年月", Assert.Single(reconfirmJan, r => r.CustomerCode == CustomerInvoice).SkipReason);

            // 2月を物理削除して1月（解除済み）を再確定 → 既存行が上書きされ確定に戻る。
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.monthly_closings WHERE closing_date = {Feb}");
            var reconfirmed = await service.ConfirmAsync(2020, 1);
            Assert.Null(Assert.Single(reconfirmed, r => r.CustomerCode == CustomerInvoice).SkipReason);
            var janRow = await dbContext.MonthlyClosings.AsNoTracking()
                .SingleAsync(m => m.ClosingDate == Jan && m.CustomerCode == CustomerInvoice);
            Assert.Equal(ClosingStatus.Confirmed, janRow.ClosingStatus);
            Assert.Null(janRow.ReleasedAt);
            Assert.Null(janRow.ReleasedBy);

            // 前月（1月）を解除済みにしてから2月を締めると飛ばされる。
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.monthly_closings SET closing_status = 2, released_at = SYSDATETIME(), released_by = 'TEST' WHERE closing_date = {Jan} AND customer_code = {CustomerInvoice}");
            var febResults = await service.ConfirmAsync(2020, 2);
            Assert.Contains("前月", Assert.Single(febResults, r => r.CustomerCode == CustomerInvoice).SkipReason);
        }
        finally
        {
            await CleanupAsync(dbContext, [slip], [], CustomerInvoice);
        }
    }

    [Fact]
    public async Task 前月残高も当月の取引もない得意先は締められない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice, RoundingType.Floor);

        try
        {
            var results = await service.ConfirmAsync(2020, 1);

            var target = Assert.Single(results, r => r.CustomerCode == CustomerInvoice);
            Assert.NotNull(target.SkipReason);
            Assert.False(await dbContext.MonthlyClosings.AnyAsync(
                m => m.ClosingDate == Jan && m.CustomerCode == CustomerInvoice));
        }
        finally
        {
            await CleanupAsync(dbContext, [], [], CustomerInvoice);
        }
    }

    private static (BmcsDbContext DbContext, MonthlyClosingService Service, CustomerLedgerQueryService Ledger) Resolve(
        AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<MonthlyClosingService>(),
        scope.ServiceProvider.GetRequiredService<CustomerLedgerQueryService>());

    private static async Task InsertCustomerAsync(
        BmcsDbContext dbContext, string customerCode, TaxUnit taxUnit, RoundingType roundingType,
        string? billingCustomerCode = null, byte closingDay = BillingClosingDay)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            SalesEmployeeCode = null,
            ClosingDay = closingDay,
            TaxUnit = taxUnit,
            RoundingType = roundingType,
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
        string customerCode, TaxUnit taxUnit, string slipNumber, short lineNumber, DateOnly slipDate,
        TaxCategory taxCategory, decimal taxRate, decimal quantity, decimal unitPrice, RoundingType roundingType)
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
            ProductCode = "1001",
            ProductName = "テスト用商品",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = ConsumptionTaxCalculator.CalculateLineAmount(quantity, unitPrice, roundingType),
            CostPrice = 700m,
            TaxCategory = taxCategory,
            TaxRate = taxRate,
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

    private static ReceiptEntity NewReceiptLine(
        string customerCode, TaxUnit taxUnit, string receiptSlipNumber, short lineNumber,
        DateOnly receiptDate, decimal amount)
    {
        var now = DateTime.Now;
        return new ReceiptEntity
        {
            ReceiptSlipNumber = receiptSlipNumber,
            LineNumber = lineNumber,
            ReceiptDate = receiptDate,
            CustomerCode = customerCode,
            TaxUnit = taxUnit,
            CustomerName = "テスト用得意先",
            DepositMethodCode = "TRANSFER",
            BankAccountCode = "1",
            Amount = amount,
            AllocationStatus = AllocationStatus.Unallocated,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// テストが作った行をFKの順に物理削除する。<c>monthly_closings</c>は2020年1〜2月分を全件消す
    /// （この月の行はテストでしか作られない）。得意先は子（請求集約元）→親（請求集約先）の順に渡す。
    /// </summary>
    private static async Task CleanupAsync(
        BmcsDbContext dbContext, IReadOnlyList<string> salesSlipNumbers, IReadOnlyList<string> receiptSlipNumbers,
        params string[] customerCodes)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.monthly_closings WHERE closing_date IN ({Jan}, {Feb})");

        foreach (var slipNumber in salesSlipNumbers)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.sales WHERE sales_slip_number = {slipNumber}");
        }

        foreach (var slipNumber in receiptSlipNumbers)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.receipts WHERE receipt_slip_number = {slipNumber}");
        }

        foreach (var customerCode in customerCodes)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.billings WHERE customer_code = {customerCode}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.customers WHERE customer_code = {customerCode}");
        }
    }
}
