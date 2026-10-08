using bmcs_app.Application.Sales;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Sales;

/// <summary>
/// 納品書の表示データ取得・発行記録の結合テスト。開発用ライブDB
/// （172.16.3.171）に対して実行する（docs/architecture.md 16章）。専用のテスト得意先
/// （<c>__TSTDN*</c>）で税単位3種を検証し、finallyで物理削除する
/// （<see cref="Billing.DetailInvoiceServiceTests"/>と同じ方式）。
/// </summary>
public class DeliveryNoteServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    [Fact]
    public async Task 請求単位は税率別内訳が空で税抜合計のみになる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        const string customerCode = "__TSTDNINV";
        const string slipNumber = "__TSTDNINVSLP";

        await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Invoice);
        dbContext.Sales.Add(NewSalesLine(
            customerCode, slipNumber, 1, TaxCategory.Standard, 10m, quantity: 5m, unitPrice: 1000m,
            slipTaxAmount: null, taxAmount: null));
        await dbContext.SaveChangesAsync();

        try
        {
            var data = await service.GetAsync(slipNumber);

            Assert.NotNull(data);
            Assert.Equal(TaxUnit.Invoice, data.TaxUnit);
            Assert.Empty(data.TaxBreakdowns);
            Assert.Equal(0m, data.TaxTotal);
            Assert.Equal(5000m, data.TaxExcludedTotal);
            Assert.Equal(5000m, data.GrandTotal);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slipNumber]);
        }
    }

    [Fact]
    public async Task 伝票単位は税率別内訳の合計が保存済みの伝票税額と一致する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        const string customerCode = "__TSTDNSLP";
        const string slipNumber = "__TSTDNSLPSLP";

        await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Slip);
        // 3702×10%=370.2→四捨五入で370。500×8%=40.0。合計410（全行同値）。
        const decimal slipTaxAmount = 410m;
        dbContext.Sales.Add(NewSalesLine(
            customerCode, slipNumber, 1, TaxCategory.Standard, 10m, quantity: 3m, unitPrice: 1234m,
            slipTaxAmount: slipTaxAmount, taxAmount: null));
        dbContext.Sales.Add(NewSalesLine(
            customerCode, slipNumber, 2, TaxCategory.Reduced, 8m, quantity: 1m, unitPrice: 500m,
            slipTaxAmount: slipTaxAmount, taxAmount: null));
        await dbContext.SaveChangesAsync();

        try
        {
            var data = await service.GetAsync(slipNumber);

            Assert.NotNull(data);
            Assert.Equal(2, data.TaxBreakdowns.Count);
            Assert.Equal(slipTaxAmount, data.TaxBreakdowns.Sum(b => b.TaxAmount));
            Assert.Equal(slipTaxAmount, data.TaxTotal);
            Assert.Equal(3702m + 500m, data.TaxExcludedTotal);
            Assert.Equal(3702m + 500m + slipTaxAmount, data.GrandTotal);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slipNumber]);
        }
    }

    [Fact]
    public async Task 内税明細単位は保存済みの行ごとの税額を集計し返品行のマイナスも反映する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        const string customerCode = "__TSTDNLIN";
        const string slipNumber = "__TSTDNLINSLP";

        await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Line);
        // 8% 内税、Amount=11000 → TaxAmount=11000*8/108=814.81…→四捨五入815。
        dbContext.Sales.Add(NewSalesLine(
            customerCode, slipNumber, 1, TaxCategory.Reduced, 8m, quantity: 20m, unitPrice: 550m,
            slipTaxAmount: null, taxAmount: 815m, amountOverride: 11000m));
        // 返品行（マイナス）。-1100*8/108=-81.48…→四捨五入-81。
        dbContext.Sales.Add(NewSalesLine(
            customerCode, slipNumber, 2, TaxCategory.Reduced, 8m, quantity: -2m, unitPrice: 550m,
            slipTaxAmount: null, taxAmount: -81m, amountOverride: -1100m, slipType: SlipType.Return));
        await dbContext.SaveChangesAsync();

        try
        {
            var data = await service.GetAsync(slipNumber);

            Assert.NotNull(data);
            Assert.Single(data.TaxBreakdowns);
            Assert.Equal(734m, data.TaxTotal); // 815 + (-81)
            Assert.Equal(9900m, data.Lines.Sum(l => l.Amount)); // 11000 - 1100
            Assert.Equal(9166m, data.TaxExcludedTotal); // 9900 - 734
            Assert.Equal(9900m, data.GrandTotal);
            Assert.Contains(data.Lines, l => l.SlipType == SlipType.Return && l.Quantity < 0);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slipNumber]);
        }
    }

    [Fact]
    public async Task 存在しない伝票番号はnullを返す()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (_, service) = Resolve(scope);

        var data = await service.GetAsync("__TSTDN_NOTFOUND");

        Assert.Null(data);
    }

    [Fact]
    public async Task 発行を記録すると全行の発行日時が更新され発行回数が加算される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        const string customerCode = "__TSTDNISS";
        const string slipNumber = "__TSTDNISSSLP";

        await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Invoice);
        dbContext.Sales.Add(NewSalesLine(customerCode, slipNumber, 1, TaxCategory.Standard, 10m, 1m, 1000m, null, null));
        dbContext.Sales.Add(NewSalesLine(customerCode, slipNumber, 2, TaxCategory.Standard, 10m, 1m, 1000m, null, null));
        await dbContext.SaveChangesAsync();

        try
        {
            await service.MarkIssuedAsync(slipNumber);

            var afterFirst = await FetchFreshAsync(scope, slipNumber);
            Assert.All(afterFirst, l => Assert.NotNull(l.DeliveryNoteIssuedAt));
            Assert.All(afterFirst, l => Assert.Equal(1, l.DeliveryNoteIssueCount));

            await service.MarkIssuedAsync(slipNumber);

            var afterSecond = await FetchFreshAsync(scope, slipNumber);
            Assert.All(afterSecond, l => Assert.Equal(2, l.DeliveryNoteIssueCount));
            Assert.True(afterSecond[0].DeliveryNoteIssuedAt >= afterFirst[0].DeliveryNoteIssuedAt);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slipNumber]);
        }
    }

    [Fact]
    public async Task 存在しない伝票の発行記録は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (_, service) = Resolve(scope);

        await Assert.ThrowsAsync<DeliveryNoteException>(() => service.MarkIssuedAsync("__TSTDN_NOTFOUND"));
    }

    /// <summary>
    /// 回帰テスト: 売上入力画面が SalesQueryService.GetSlipAsync（追跡あり）で読み込んだ後に
    /// 同じ DbContext スコープで納品書を発行しても、追跡中エンティティの RowVersion が
    /// 陳腐化せず、その後の保存が偽の競合エラーにならないこと。
    /// </summary>
    [Fact]
    public async Task 追跡中の売上行に対して発行記録しても後続の保存が競合エラーにならない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        const string customerCode = "__TSTDNTRK";
        const string slipNumber = "__TSTDNTRKSLP";

        await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Invoice);
        dbContext.Sales.Add(NewSalesLine(customerCode, slipNumber, 1, TaxCategory.Standard, 10m, 1m, 1000m, null, null));
        await dbContext.SaveChangesAsync();

        try
        {
            // 売上入力画面の SalesQueryService.GetSlipAsync と同じく、AsNoTracking を付けずに
            // 同一 DbContext で読み込む（追跡状態になる）。
            var trackedLines = await dbContext.Sales
                .Where(s => s.SalesSlipNumber == slipNumber && !s.IsDeleted)
                .ToListAsync();
            var rowVersionBeforeIssue = trackedLines[0].RowVersion;

            await service.MarkIssuedAsync(slipNumber);

            // ExecuteUpdateAsync は ChangeTracker を経由しないため、対策が無ければ
            // trackedLines[0].RowVersion はここで古い値のままになる。
            Assert.NotEqual(rowVersionBeforeIssue, trackedLines[0].RowVersion);

            // 訂正保存を模して、追跡中のエンティティをそのまま更新する。
            // RowVersion が最新化されていなければ DbUpdateConcurrencyException になる。
            trackedLines[0].LineRemarks = "訂正テスト";
            trackedLines[0].UpdatedBy = "TEST";
            trackedLines[0].UpdatedAt = DateTime.Now;
            var exception = await Record.ExceptionAsync(() => dbContext.SaveChangesAsync());

            Assert.Null(exception);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slipNumber]);
        }
    }

    private static (BmcsDbContext DbContext, DeliveryNoteService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<DeliveryNoteService>());

    private static async Task<List<SalesEntity>> FetchFreshAsync(AsyncServiceScope scope, string slipNumber)
    {
        await using var freshScope = scope.ServiceProvider.CreateAsyncScope();
        var dbContext = freshScope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        return await dbContext.Sales
            .AsNoTracking()
            .Where(s => s.SalesSlipNumber == slipNumber)
            .OrderBy(s => s.LineNumber)
            .ToListAsync();
    }

    private static async Task InsertCustomerAsync(BmcsDbContext dbContext, string customerCode, TaxUnit taxUnit)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            SalesEmployeeCode = "EMP001",
            ClosingDay = taxUnit == TaxUnit.Line ? (byte)0 : (byte)20,
            TaxUnit = taxUnit,
            RoundingType = RoundingType.RoundHalfUp,
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
        string customerCode,
        string slipNumber,
        short lineNumber,
        TaxCategory taxCategory,
        decimal taxRate,
        decimal quantity,
        decimal unitPrice,
        decimal? slipTaxAmount,
        decimal? taxAmount,
        decimal? amountOverride = null,
        SlipType slipType = SlipType.Sales)
    {
        var now = DateTime.Now;
        var amount = amountOverride ?? quantity * unitPrice;
        var taxUnit = slipTaxAmount is not null ? TaxUnit.Slip : taxAmount is not null ? TaxUnit.Line : TaxUnit.Invoice;

        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = lineNumber,
            SlipDate = new DateOnly(2026, 8, 1),
            CustomerCode = customerCode,
            TaxUnit = taxUnit,
            CustomerName = "テスト用得意先",
            SlipType = slipType,
            ProductCode = "PRD001",
            ProductName = "テスト用商品",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = amount,
            CostPrice = 0m,
            TaxCategory = taxCategory,
            TaxRate = taxRate,
            SlipTaxAmount = slipTaxAmount,
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

    private static async Task CleanupAsync(
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
                $"DELETE FROM dbo.customers WHERE customer_code = {customerCode}");
        }
    }
}
