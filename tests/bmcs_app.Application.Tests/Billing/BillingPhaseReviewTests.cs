using bmcs_app.Application.Billing;
using bmcs_app.Application.Sales;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Billing;

/// <summary>
/// Phase 6-5「フェーズレビュー（二重計上・状態整合）」の結合テスト。開発用ライブDB
/// （172.16.3.171）に対して実行する（docs/architecture.md 16章）。
/// </summary>
/// <remarks>
/// 締め・解除・発行・取消（6-1〜6-4）は単体では既存テスト（<see cref="BillingClosingServiceTests"/>／
/// <see cref="BillingReleaseServiceTests"/>／<see cref="DetailInvoiceServiceTests"/>）で検証済み。
/// 本ファイルは①締め請求と明細請求が同じ売上を二重に拾わないこと、②請求後の売上訂正・取消
/// （Phase 5-6）との相互作用、③Phase 6-5で追加した第4の編集ロック条件（明細請求書発行済み）
/// を検証する。<c>BillingClosingServiceTests</c>と同じ「専用のテスト得意先で確定した後、
/// finallyで物理削除する」方式を採る。seedの得意先・既存の専用テスト得意先（closing_day = 15, 16）
/// とは重ならない<c>closing_day = 17</c>を使う。
/// </remarks>
public class BillingPhaseReviewTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const byte TestClosingDay = 17;

    [Fact]
    public async Task 締め請求と明細請求は互いの対象を拾わない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, _, detailInvoiceService, _) = Resolve(scope);

        var invoiceCustomer = "__TPHR01";
        var lineCustomer = "__TPHR02";
        await InsertCustomerAsync(dbContext, invoiceCustomer, TaxUnit.Invoice, TestClosingDay, RoundingType.Floor);
        // 内税明細単位は closing_day = 0 が相互制約（CK_customer_tax_unit_closing_day）。
        await InsertCustomerAsync(dbContext, lineCustomer, TaxUnit.Line, closingDay: 0, RoundingType.Floor);

        var invoiceSlip = "__TPHR01_INV";
        var lineSlip = "__TPHR02_LIN";
        dbContext.Sales.Add(NewInvoiceOrSlipLine(
            invoiceCustomer, TaxUnit.Invoice, invoiceSlip, 1, new DateOnly(2025, 7, 1),
            quantity: 1m, unitPrice: 1000m, RoundingType.Floor));
        dbContext.Sales.Add(NewLineUnitLine(
            lineCustomer, lineSlip, 1, new DateOnly(2025, 7, 1), quantity: 1m, unitPrice: 110m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            // 請求単位の得意先の売上は、明細請求書の候補には現れない（tax_unit=3限定のBuildCandidateQuery）。
            var detailCandidatesForInvoiceCustomer = await detailInvoiceService.GetCandidatesAsync(invoiceCustomer);
            Assert.Empty(detailCandidatesForInvoiceCustomer);

            // 内税明細単位の得意先は締め対象の得意先抽出（TaxUnit.Invoice/Slip限定）に出てこないため、
            // closing_day = 0 で締めを走らせても対象にならない。
            var closingPreviewForLineCustomer = await closingService.PreviewAsync(0, new DateOnly(2025, 7, 15));
            Assert.DoesNotContain(closingPreviewForLineCustomer, r => r.CustomerCode == lineCustomer);

            // 逆方向: 内税明細単位の得意先の売上は明細請求書の候補に現れる。
            var detailCandidatesForLineCustomer = await detailInvoiceService.GetCandidatesAsync(lineCustomer);
            Assert.Single(detailCandidatesForLineCustomer, c => c.SalesSlipNumber == lineSlip);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.detail_invoice_sales_line WHERE sales_slip_number = {lineSlip}");
            await CleanupSalesAndCustomersAsync(dbContext, [invoiceCustomer, lineCustomer], [invoiceSlip, lineSlip]);
        }
    }

    [Fact]
    public async Task 締め_解除_売上訂正_再締めで訂正後の金額が二重計上されずに確定する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService, _, salesService) = Resolve(scope);

        var customerCode = "__TPHR03";
        await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Invoice, TestClosingDay, RoundingType.Floor);

        var slip = "__TPHR03_SLIP";
        dbContext.Sales.Add(NewInvoiceOrSlipLine(
            customerCode, TaxUnit.Invoice, slip, 1, new DateOnly(2025, 7, 1),
            quantity: 5m, unitPrice: 1000m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            var firstResults = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var first = Assert.Single(firstResults, r => r.CustomerCode == customerCode);
            Assert.Equal(5000m, first.SalesAmount);

            await releaseService.ReleaseByBillingDateAsync(new DateOnly(2025, 7, 15));

            var currentLine = await dbContext.Sales.SingleAsync(s => s.SalesSlipNumber == slip);
            var incoming = NewInvoiceOrSlipLine(
                customerCode, TaxUnit.Invoice, slip, 1, new DateOnly(2025, 7, 1),
                quantity: 10m, unitPrice: 1000m, RoundingType.Floor);
            incoming.LineNumber = currentLine.LineNumber;

            await salesService.UpdateAsync(slip, [incoming], RoundingType.Floor, [currentLine.LineNumber]);

            var secondResults = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var second = Assert.Single(secondResults, r => r.CustomerCode == customerCode);

            Assert.NotEqual(first.BillingNumber, second.BillingNumber);
            Assert.Equal(10000m, second.SalesAmount); // 訂正前(5000)ではなく訂正後の金額
            Assert.Equal(1000m, second.TaxAmount);
            Assert.Equal(11000m, second.CurrentBillingAmount);
        }
        finally
        {
            await CleanupSalesAndCustomersAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 発行_取消_売上訂正_再発行で訂正後の金額が二重計上されずに確定する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, _, _, detailInvoiceService, salesService) = Resolve(scope);

        var customerCode = "__TPHR04";
        await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Line, closingDay: 0, RoundingType.Floor);

        var slip = "__TPHR04_SLIP";
        dbContext.Sales.Add(NewLineUnitLine(
            customerCode, slip, 1, new DateOnly(2025, 7, 1), quantity: 1m, unitPrice: 110m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        string? secondInvoiceNumber = null;
        try
        {
            var issued = await detailInvoiceService.IssueAsync(
                customerCode, "宛名", new DateOnly(2025, 7, 10), [(slip, (short)1)]);
            Assert.Equal(100.00m, issued.SalesAmount); // 110円(税込10%) → 税抜100

            await detailInvoiceService.CancelAsync(issued.DetailInvoiceNumber);

            var currentLine = await dbContext.Sales.SingleAsync(s => s.SalesSlipNumber == slip);
            var incoming = NewLineUnitLine(
                customerCode, slip, 1, new DateOnly(2025, 7, 1), quantity: 2m, unitPrice: 110m, RoundingType.Floor);
            incoming.LineNumber = currentLine.LineNumber;

            await salesService.UpdateAsync(slip, [incoming], RoundingType.Floor, [currentLine.LineNumber]);

            var reissued = await detailInvoiceService.IssueAsync(
                customerCode, "宛名2", new DateOnly(2025, 7, 11), [(slip, (short)1)]);
            secondInvoiceNumber = reissued.DetailInvoiceNumber;

            Assert.NotEqual(issued.DetailInvoiceNumber, reissued.DetailInvoiceNumber);
            Assert.Equal(200.00m, reissued.SalesAmount); // 訂正前(100)ではなく訂正後(数量2)の金額
        }
        finally
        {
            if (secondInvoiceNumber is not null)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM dbo.detail_invoice_sales_line WHERE detail_invoice_number = {secondInvoiceNumber}");
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM dbo.detail_invoice WHERE detail_invoice_number = {secondInvoiceNumber}");
            }
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.detail_invoice_sales_line WHERE sales_slip_number = {slip}");
            await CleanupSalesAndCustomersAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 請求締め済みの売上は訂正も取消もできない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, _, _, salesService) = Resolve(scope);

        var customerCode = "__TPHR05";
        await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Invoice, TestClosingDay, RoundingType.Floor);

        var slip = "__TPHR05_SLIP";
        dbContext.Sales.Add(NewInvoiceOrSlipLine(
            customerCode, TaxUnit.Invoice, slip, 1, new DateOnly(2025, 7, 1),
            quantity: 1m, unitPrice: 1000m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));

            var currentLine = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slip);
            var incoming = NewInvoiceOrSlipLine(
                customerCode, TaxUnit.Invoice, slip, 1, new DateOnly(2025, 7, 1),
                quantity: 2m, unitPrice: 1000m, RoundingType.Floor);
            incoming.LineNumber = currentLine.LineNumber;

            await Assert.ThrowsAsync<SalesOperationException>(
                () => salesService.UpdateAsync(slip, [incoming], RoundingType.Floor, [currentLine.LineNumber]));
            await Assert.ThrowsAsync<SalesOperationException>(() => salesService.CancelSlipAsync(slip));
        }
        finally
        {
            await CleanupSalesAndCustomersAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 明細請求書発行済みの売上は訂正も取消もできず取消後は再び編集できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, _, _, detailInvoiceService, salesService) = Resolve(scope);

        var customerCode = "__TPHR06";
        await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Line, closingDay: 0, RoundingType.Floor);

        var slip = "__TPHR06_SLIP";
        dbContext.Sales.Add(NewLineUnitLine(
            customerCode, slip, 1, new DateOnly(2025, 7, 1), quantity: 1m, unitPrice: 110m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            var issued = await detailInvoiceService.IssueAsync(
                customerCode, "宛名", new DateOnly(2025, 7, 10), [(slip, (short)1)]);

            var currentLine = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slip);
            var incoming = NewLineUnitLine(
                customerCode, slip, 1, new DateOnly(2025, 7, 1), quantity: 2m, unitPrice: 110m, RoundingType.Floor);
            incoming.LineNumber = currentLine.LineNumber;

            var updateEx = await Assert.ThrowsAsync<SalesOperationException>(
                () => salesService.UpdateAsync(slip, [incoming], RoundingType.Floor, [currentLine.LineNumber]));
            Assert.Contains("明細請求書", updateEx.Message);
            await Assert.ThrowsAsync<SalesOperationException>(() => salesService.CancelSlipAsync(slip));

            await detailInvoiceService.CancelAsync(issued.DetailInvoiceNumber);

            // 取消後は再び編集できる（回帰確認: ロックが取消済みの明細請求書には効かない）。
            var currentLineAfterCancel = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slip);
            var incomingAfterCancel = NewLineUnitLine(
                customerCode, slip, 1, new DateOnly(2025, 7, 1), quantity: 3m, unitPrice: 110m, RoundingType.Floor);
            incomingAfterCancel.LineNumber = currentLineAfterCancel.LineNumber;

            await salesService.UpdateAsync(
                slip, [incomingAfterCancel], RoundingType.Floor, [currentLineAfterCancel.LineNumber]);

            var updated = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slip);
            Assert.Equal(330m, updated.Amount);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.detail_invoice_sales_line WHERE sales_slip_number = {slip}");
            await CleanupSalesAndCustomersAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 締め_解除_再締めを繰り返しても確定済みの請求データは常に1件だけになる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService, _, _) = Resolve(scope);

        var customerCode = "__TPHR07";
        await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Invoice, TestClosingDay, RoundingType.Floor);

        var slip = "__TPHR07_SLIP";
        dbContext.Sales.Add(NewInvoiceOrSlipLine(
            customerCode, TaxUnit.Invoice, slip, 1, new DateOnly(2025, 7, 1),
            quantity: 1m, unitPrice: 1000m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            var first = Assert.Single(
                await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15)),
                r => r.CustomerCode == customerCode);
            await releaseService.ReleaseByBillingDateAsync(new DateOnly(2025, 7, 15));
            var second = Assert.Single(
                await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15)),
                r => r.CustomerCode == customerCode);

            var confirmedBillings = await dbContext.Billings.AsNoTracking()
                .Where(b => b.CustomerCode == customerCode && !b.IsDeleted && b.BillingStatus == BillingStatus.Confirmed)
                .ToListAsync();

            var confirmed = Assert.Single(confirmedBillings);
            Assert.Equal(second.BillingNumber, confirmed.BillingNumber);
        }
        finally
        {
            await CleanupSalesAndCustomersAsync(dbContext, [customerCode], [slip]);
        }
    }

    private static (
        BmcsDbContext DbContext,
        BillingClosingService ClosingService,
        BillingReleaseService ReleaseService,
        DetailInvoiceService DetailInvoiceService,
        SalesService SalesService) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<BillingClosingService>(),
        scope.ServiceProvider.GetRequiredService<BillingReleaseService>(),
        scope.ServiceProvider.GetRequiredService<DetailInvoiceService>(),
        scope.ServiceProvider.GetRequiredService<SalesService>());

    private static async Task InsertCustomerAsync(
        BmcsDbContext dbContext, string customerCode, TaxUnit taxUnit, byte closingDay, RoundingType roundingType)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            SalesEmployeeCode = "EMP001",
            ClosingDay = closingDay,
            TaxUnit = taxUnit,
            RoundingType = roundingType,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await dbContext.SaveChangesAsync();
    }

    private static SalesEntity NewInvoiceOrSlipLine(
        string customerCode, TaxUnit taxUnit, string slipNumber, short lineNumber, DateOnly slipDate,
        decimal quantity, decimal unitPrice, RoundingType roundingType)
    {
        var amount = ConsumptionTaxCalculator.CalculateLineAmount(quantity, unitPrice, roundingType);
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
            UnitPrice = unitPrice,
            Amount = amount,
            CostPrice = 700m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            SlipTaxAmount = taxUnit == TaxUnit.Slip
                ? ConsumptionTaxCalculator.CalculateSlipTaxAmount([new TaxLine(TaxCategory.Standard, 10m, amount)], roundingType)
                : null,
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

    private static SalesEntity NewLineUnitLine(
        string customerCode, string slipNumber, short lineNumber, DateOnly slipDate,
        decimal quantity, decimal unitPrice, RoundingType roundingType)
    {
        var amount = ConsumptionTaxCalculator.CalculateLineAmount(quantity, unitPrice, roundingType);
        var taxAmount = ConsumptionTaxCalculator.CalculateInternalTaxAmount(
            new TaxLine(TaxCategory.Standard, 10m, amount), roundingType);
        var now = DateTime.Now;

        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = lineNumber,
            SlipDate = slipDate,
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
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            SlipTaxAmount = null,
            TaxAmount = taxAmount,
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

    private static async Task CleanupSalesAndCustomersAsync(
        BmcsDbContext dbContext, IReadOnlyList<string> customerCodes, IReadOnlyList<string> salesSlipNumbers)
    {
        foreach (var slipNumber in salesSlipNumbers)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.sales WHERE sales_slip_number = {slipNumber}");
        }

        foreach (var customerCode in customerCodes)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.billing WHERE customer_code = {customerCode}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.detail_invoice WHERE customer_code = {customerCode}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.customer WHERE customer_code = {customerCode}");
        }
    }
}
