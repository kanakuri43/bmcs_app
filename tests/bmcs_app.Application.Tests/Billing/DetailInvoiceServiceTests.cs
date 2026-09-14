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
/// 明細請求書発行（TODO.md 6-3）の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する
/// （docs/architecture.md 16章）。完了条件「対象条件が明細行単位で正しく効いている」の実証。
/// </summary>
/// <remarks>
/// <see cref="BillingClosingServiceTests"/>／<see cref="BillingReleaseServiceTests"/>と同じ
/// 「専用のテスト得意先で発行した後、finallyで物理削除する」方式を採る
/// （<see cref="DetailInvoiceService.IssueAsync"/>が内部で<c>BeginTransactionAsync</c>するため）。
/// seedの都度得意先 CUS003 とは重ならない専用得意先（<c>__TSTDIV1</c>／<c>__TSTDIV2</c>）を使う。
/// </remarks>
public class DetailInvoiceServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerCode = "__TSTDIV1";
    private const string OtherCustomerCode = "__TSTDIV2";

    [Fact]
    public async Task 候補抽出は未請求かつ消込完了でない売上明細行だけを返す()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);
        await InsertCustomerAsync(dbContext, OtherCustomerCode);

        var slipCandidate = "__TSTDIV_CAND";
        var slipAlreadyLinked = "__TSTDIV_LINKED";
        var slipSettled = "__TSTDIV_SETTLED";
        var slipDeleted = "__TSTDIV_DELETED";
        var slipOtherCustomer = "__TSTDIV_OTHER";

        dbContext.Sales.Add(NewSalesLine(CustomerCode, slipCandidate, 1, new DateOnly(2025, 7, 1), 1m, 110m, TaxCategory.Standard, 10m, RoundingType.Floor));
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slipAlreadyLinked, 1, new DateOnly(2025, 7, 1), 1m, 110m, TaxCategory.Standard, 10m, RoundingType.Floor));
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slipSettled, 1, new DateOnly(2025, 7, 1), 1m, 110m, TaxCategory.Standard, 10m, RoundingType.Floor,
            settlementStatus: SettlementStatus.FullySettled));
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slipDeleted, 1, new DateOnly(2025, 7, 1), 1m, 110m, TaxCategory.Standard, 10m, RoundingType.Floor,
            isDeleted: true));
        dbContext.Sales.Add(NewSalesLine(OtherCustomerCode, slipOtherCustomer, 1, new DateOnly(2025, 7, 1), 1m, 110m, TaxCategory.Standard, 10m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            // slipAlreadyLinked は先に発行しておき、「連携済み」の除外条件を実データで再現する。
            await service.IssueAsync(CustomerCode, "宛名A", new DateOnly(2025, 7, 10), [(slipAlreadyLinked, (short)1)]);

            var candidates = await service.GetCandidatesAsync(CustomerCode);

            Assert.Single(candidates, c => c.SalesSlipNumber == slipCandidate);
            Assert.DoesNotContain(candidates, c => c.SalesSlipNumber == slipAlreadyLinked);
            Assert.DoesNotContain(candidates, c => c.SalesSlipNumber == slipSettled);
            Assert.DoesNotContain(candidates, c => c.SalesSlipNumber == slipDeleted);
            Assert.DoesNotContain(candidates, c => c.SalesSlipNumber == slipOtherCustomer);
        }
        finally
        {
            await CleanupAsync(dbContext, [CustomerCode, OtherCustomerCode],
                [slipCandidate, slipAlreadyLinked, slipSettled, slipDeleted, slipOtherCustomer]);
        }
    }

    [Fact]
    public async Task 発行すると明細請求書と連携行が作られ売上が請求済になるが請求番号はNULLのまま()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        // qty10×税込110円@標準10% → 税抜1000/税額100。qty10×税込108円@軽減8% → 税抜1000/税額80。
        var slip1 = "__TSTDIV_ISSUE1";
        var slip2 = "__TSTDIV_ISSUE2";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slip1, 1, new DateOnly(2025, 7, 1), 10m, 110m, TaxCategory.Standard, 10m, RoundingType.Floor));
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slip2, 1, new DateOnly(2025, 7, 1), 10m, 108m, TaxCategory.Reduced, 8m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            var issued = await service.IssueAsync(
                CustomerCode, "石山小学校5年2組 山田先生", new DateOnly(2025, 7, 10),
                [(slip1, (short)1), (slip2, (short)1)]);

            Assert.Equal(2000.00m, issued.SalesAmount);
            Assert.Equal(180.00m, issued.TaxAmount);
            Assert.Equal(2180.00m, issued.TotalAmount);
            Assert.Equal(1000.00m, issued.StandardRateTaxableAmount);
            Assert.Equal(100.00m, issued.StandardRateTaxAmount);
            Assert.Equal(1000.00m, issued.ReducedRateTaxableAmount);
            Assert.Equal(80.00m, issued.ReducedRateTaxAmount);
            Assert.Equal(0.00m, issued.TaxExemptAmount);
            Assert.Equal("石山小学校5年2組 山田先生", issued.AddresseeName);
            Assert.Equal("テスト用都度得意先", issued.CustomerName);
            Assert.Equal(DetailInvoiceStatus.Issued, issued.InvoiceStatus);

            var links = await dbContext.DetailInvoiceSalesLines.AsNoTracking()
                .Where(l => l.DetailInvoiceNumber == issued.DetailInvoiceNumber)
                .ToListAsync();
            Assert.Equal(2, links.Count);
            Assert.Contains(links, l => l.SalesSlipNumber == slip1);
            Assert.Contains(links, l => l.SalesSlipNumber == slip2);

            var persistedSales = await dbContext.Sales.AsNoTracking()
                .Where(s => s.SalesSlipNumber == slip1 || s.SalesSlipNumber == slip2)
                .ToListAsync();
            Assert.All(persistedSales, s =>
            {
                Assert.Equal(BillingLinkStatus.Billed, s.BillingStatus);
                Assert.Null(s.BillingNumber);
            });
        }
        finally
        {
            await CleanupAsync(dbContext, [CustomerCode], [slip1, slip2]);
        }
    }

    [Fact]
    public async Task 発行済みの売上明細行は次回の候補に出ない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTDIV_NOREPEAT";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slip, 1, new DateOnly(2025, 7, 1), 1m, 110m, TaxCategory.Standard, 10m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            var beforeIssue = await service.GetCandidatesAsync(CustomerCode);
            Assert.Contains(beforeIssue, c => c.SalesSlipNumber == slip);

            await service.IssueAsync(CustomerCode, "宛名", new DateOnly(2025, 7, 10), [(slip, (short)1)]);

            var afterIssue = await service.GetCandidatesAsync(CustomerCode);
            Assert.DoesNotContain(afterIssue, c => c.SalesSlipNumber == slip);
        }
        finally
        {
            await CleanupAsync(dbContext, [CustomerCode], [slip]);
        }
    }

    [Fact]
    public async Task 返品行を含めても金額と消費税が正しく打ち消される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        // 売上: qty5×税込110円@標準10% → 税抜500/税額50。
        // 返品: qty-2×税込110円@標準10% → 税抜-200/税額-20（絶対値を丸めて符号を戻すため、
        // 除算が割り切れるこの例では通常の売上と同じ規則で打ち消される）。
        var slipSale = "__TSTDIV_RETSALE";
        var slipReturn = "__TSTDIV_RETRETURN";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slipSale, 1, new DateOnly(2025, 7, 1), 5m, 110m, TaxCategory.Standard, 10m, RoundingType.Floor));
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slipReturn, 1, new DateOnly(2025, 7, 2), -2m, 110m, TaxCategory.Standard, 10m, RoundingType.Floor,
            slipType: SlipType.Return));
        await dbContext.SaveChangesAsync();

        try
        {
            var issued = await service.IssueAsync(
                CustomerCode, "宛名", new DateOnly(2025, 7, 10), [(slipSale, (short)1), (slipReturn, (short)1)]);

            Assert.Equal(300.00m, issued.SalesAmount);
            Assert.Equal(30.00m, issued.TaxAmount);
            Assert.Equal(330.00m, issued.TotalAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, [CustomerCode], [slipSale, slipReturn]);
        }
    }

    [Fact]
    public async Task 消込完了済みの行を指定すると例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTDIV_SETISSUE";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slip, 1, new DateOnly(2025, 7, 1), 1m, 110m, TaxCategory.Standard, 10m, RoundingType.Floor,
            settlementStatus: SettlementStatus.FullySettled));
        await dbContext.SaveChangesAsync();

        try
        {
            await Assert.ThrowsAsync<DetailInvoiceException>(
                () => service.IssueAsync(CustomerCode, "宛名", new DateOnly(2025, 7, 10), [(slip, (short)1)]));
        }
        finally
        {
            await CleanupAsync(dbContext, [CustomerCode], [slip]);
        }
    }

    [Fact]
    public async Task 既に他の明細請求書に紐づく行を再度指定すると例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTDIV_DOUBLE";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slip, 1, new DateOnly(2025, 7, 1), 1m, 110m, TaxCategory.Standard, 10m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            await service.IssueAsync(CustomerCode, "宛名1", new DateOnly(2025, 7, 10), [(slip, (short)1)]);

            await Assert.ThrowsAsync<DetailInvoiceException>(
                () => service.IssueAsync(CustomerCode, "宛名2", new DateOnly(2025, 7, 11), [(slip, (short)1)]));
        }
        finally
        {
            await CleanupAsync(dbContext, [CustomerCode], [slip]);
        }
    }

    [Fact]
    public async Task 明細が0件だと例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (_, service) = Resolve(scope);

        await Assert.ThrowsAsync<DetailInvoiceException>(
            () => service.IssueAsync(CustomerCode, "宛名", new DateOnly(2025, 7, 10), []));
    }

    [Fact]
    public async Task 内税明細単位以外の得意先を指定すると例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (_, service) = Resolve(scope);

        // CUS001はseedの締め得意先（請求単位）。存在確認済みの行を要求する必要はなく、
        // 得意先の税区分チェックが明細行の探索より先に働くことを確認する。
        await Assert.ThrowsAsync<DetailInvoiceException>(
            () => service.IssueAsync("CUS001", "宛名", new DateOnly(2025, 7, 10), [("__NOTEXIST", (short)1)]));
    }

    private static (BmcsDbContext DbContext, DetailInvoiceService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<DetailInvoiceService>());

    private static async Task InsertCustomerAsync(
        BmcsDbContext dbContext, string customerCode, RoundingType roundingType = RoundingType.Floor)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用都度得意先",
            SalesEmployeeCode = "EMP001",
            ClosingDay = 0,
            TaxUnit = TaxUnit.Line,
            RoundingType = roundingType,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await dbContext.SaveChangesAsync();
    }

    private static SalesEntity NewSalesLine(
        string customerCode,
        string slipNumber,
        short lineNumber,
        DateOnly slipDate,
        decimal quantity,
        decimal unitPrice,
        TaxCategory taxCategory,
        decimal taxRate,
        RoundingType roundingType,
        SlipType slipType = SlipType.Sales,
        SettlementStatus settlementStatus = SettlementStatus.Unsettled,
        bool isDeleted = false)
    {
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
            CustomerName = "テスト用都度得意先",
            SlipType = slipType,
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
            SettlementStatus = settlementStatus,
            SettledAmount = 0m,
            BillingNumber = null,
            IsDeleted = isDeleted,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    private static async Task CleanupAsync(
        BmcsDbContext dbContext, IReadOnlyList<string> customerCodes, IReadOnlyList<string> salesSlipNumbers)
    {
        foreach (var slipNumber in salesSlipNumbers)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.detail_invoice_sales_line WHERE sales_slip_number = {slipNumber}");
        }

        foreach (var customerCode in customerCodes)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.detail_invoice WHERE customer_code = {customerCode}");
        }

        foreach (var slipNumber in salesSlipNumbers)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.sales WHERE sales_slip_number = {slipNumber}");
        }

        foreach (var customerCode in customerCodes)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.customer WHERE customer_code = {customerCode}");
        }
    }
}
