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
/// 締め解除処理（TODO.md 6-2、2026-09-15改訂で請求日単位の一括解除に変更）の結合テスト。
/// 開発用ライブDB（172.16.3.171）に対して実行する（docs/architecture.md 16章）。
/// 完了条件「解除→再締めで金額が一致する」の実証。
/// </summary>
/// <remarks>
/// <see cref="BillingClosingServiceTests"/>と同じ「専用のテスト得意先で確定した後、
/// finallyで物理削除する」方式を採る。seedの得意先・<see cref="BillingClosingServiceTests"/>の
/// 専用得意先（closing_day = 15）とも重ならない<c>closing_day = 16</c>を使う。請求日も
/// <c>BillingClosingServiceTests</c>が使う<c>2025/07/15</c>系と重ならないよう<c>2025/07/16</c>系にする。
/// </remarks>
public class BillingReleaseServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerCode = "__TSTREL1";
    private const string CustomerCode2 = "__TSTREL2";
    private const byte TestClosingDay = 16;

    /// <summary>
    /// All-or-nothingのテスト専用。<see cref="CustomerCode2"/>だけ別の締め日区分にすることで、
    /// 「2025/08/16に新しい締めを確定する」操作が<see cref="CustomerCode"/>側の残高繰越を
    /// 巻き込まないようにする（同じ締め日区分だと、新規売上がなくても前回残高がある得意先は
    /// 一緒に再確定されてしまい、意図した締め順序逆転の状況を作れないため）。
    /// <see cref="BillingPhaseReviewTests"/>が使う<c>closing_day = 17</c>と値が重複するが、
    /// <c>closing_day</c>自体はユニーク制約ではなく得意先コードで隔離されるため問題ない。
    /// </summary>
    private const byte TestClosingDay2 = 17;

    [Fact]
    public async Task 確定済み請求データを解除すると解除済になり紐づく売上が未請求に戻る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTREL_SLP01";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slip, 1, new DateOnly(2025, 7, 1), quantity: 5m, unitPrice: 1000m));
        await dbContext.SaveChangesAsync();

        try
        {
            var confirmed = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 16));
            var target = Assert.Single(confirmed, r => r.CustomerCode == CustomerCode);
            var billingNumber = target.BillingNumber!;

            var released = await releaseService.ReleaseByBillingDateAsync(new DateOnly(2025, 7, 16));

            var releasedTarget = Assert.Single(released, r => r.CustomerCode == CustomerCode);
            Assert.Equal(billingNumber, releasedTarget.BillingNumber);

            var persistedBilling = await dbContext.Billings.AsNoTracking()
                .SingleAsync(b => b.BillingNumber == billingNumber);
            Assert.Equal(BillingStatus.Released, persistedBilling.BillingStatus);
            Assert.NotNull(persistedBilling.ReleasedAt);
            Assert.NotNull(persistedBilling.ReleasedBy);

            var persistedSales = await dbContext.Sales.AsNoTracking()
                .SingleAsync(s => s.SalesSlipNumber == slip);
            Assert.Null(persistedSales.BillingNumber);
            Assert.Equal(BillingLinkStatus.Unbilled, persistedSales.BillingStatus);
        }
        finally
        {
            await CleanupAsync(dbContext, [CustomerCode], [slip]);
        }
    }

    [Fact]
    public async Task 締め解除すると解除対象の売上行の消込状態も未消込へ戻る()
    {
        // TODO.md 7-1レビューで発見した既存不整合の修正確認: 解除前にbilling_numberが外れる
        // ことだけを見ていたため、消込キャッシュ列（settlement_status/settled_amount）が
        // 消込完了のまま取り残されていた。BillingReleaseService.ReleaseByBillingDateAsyncに
        // SettlementService.RecalculateForBillingGroupAsyncを配線したことで解消したことを確認する。
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTREL_SETTLED";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slip, 1, new DateOnly(2025, 7, 1), quantity: 5m, unitPrice: 1000m));
        await dbContext.SaveChangesAsync();

        try
        {
            await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 16));

            // 実際の入金（Phase 7-2未実装）を経ずに、消込完了済みの状態を直接再現する。
            var salesLine = await dbContext.Sales.SingleAsync(s => s.SalesSlipNumber == slip);
            salesLine.SettlementStatus = SettlementStatus.FullySettled;
            salesLine.SettledAmount = salesLine.Amount;
            await dbContext.SaveChangesAsync();

            await releaseService.ReleaseByBillingDateAsync(new DateOnly(2025, 7, 16));

            var persistedSales = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slip);
            Assert.Equal(SettlementStatus.Unsettled, persistedSales.SettlementStatus);
            Assert.Equal(0m, persistedSales.SettledAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, [CustomerCode], [slip]);
        }
    }

    [Fact]
    public async Task 既に解除済みの請求日を再度解除しようとすると例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTREL_SLP02";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slip, 1, new DateOnly(2025, 7, 1), quantity: 1m, unitPrice: 1000m));
        await dbContext.SaveChangesAsync();

        try
        {
            await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 16));

            await releaseService.ReleaseByBillingDateAsync(new DateOnly(2025, 7, 16));

            await Assert.ThrowsAsync<BillingReleaseException>(
                () => releaseService.ReleaseByBillingDateAsync(new DateOnly(2025, 7, 16)));
        }
        finally
        {
            await CleanupAsync(dbContext, [CustomerCode], [slip]);
        }
    }

    [Fact]
    public async Task 対象がない請求日を指定すると例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (_, _, releaseService) = Resolve(scope);

        await Assert.ThrowsAsync<BillingReleaseException>(
            () => releaseService.ReleaseByBillingDateAsync(new DateOnly(1999, 1, 1)));
    }

    [Fact]
    public async Task より新しい確定済み請求データがある場合は古い方を解除できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slipA = "__TSTREL_SEQ_A";
        var slipB = "__TSTREL_SEQ_B";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slipA, 1, new DateOnly(2025, 7, 1), quantity: 10m, unitPrice: 1000m));
        await dbContext.SaveChangesAsync();

        try
        {
            var resultsA = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 16));
            var billingA = Assert.Single(resultsA, r => r.CustomerCode == CustomerCode).BillingNumber!;

            dbContext.Sales.Add(NewSalesLine(CustomerCode, slipB, 1, new DateOnly(2025, 8, 1), quantity: 1m, unitPrice: 2000m));
            await dbContext.SaveChangesAsync();

            var resultsB = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 8, 16));
            var billingB = Assert.Single(resultsB, r => r.CustomerCode == CustomerCode).BillingNumber!;

            // billingA（古い方）はbillingB（新しい方）が確定済みのままだと解除できない。
            var ex = await Assert.ThrowsAsync<BillingReleaseException>(
                () => releaseService.ReleaseByBillingDateAsync(new DateOnly(2025, 7, 16)));
            Assert.Contains(billingB, ex.Message);

            // billingBが変更されていないこと（All-or-nothingではなく、そもそもbillingAだけが
            // 対象の請求日を指定しているため、この呼び出し自体はbillingBに触れない）。
            var persistedB = await dbContext.Billings.AsNoTracking().SingleAsync(b => b.BillingNumber == billingB);
            Assert.Equal(BillingStatus.Confirmed, persistedB.BillingStatus);

            // billingB（最新）はそのまま解除できる。
            await releaseService.ReleaseByBillingDateAsync(new DateOnly(2025, 8, 16));
            var releasedB = await dbContext.Billings.AsNoTracking().SingleAsync(b => b.BillingNumber == billingB);
            Assert.Equal(BillingStatus.Released, releasedB.BillingStatus);

            // billingBが解除されたことで、billingAが再び最新の確定済みとなり解除できる。
            await releaseService.ReleaseByBillingDateAsync(new DateOnly(2025, 7, 16));
            var releasedA = await dbContext.Billings.AsNoTracking().SingleAsync(b => b.BillingNumber == billingA);
            Assert.Equal(BillingStatus.Released, releasedA.BillingStatus);
        }
        finally
        {
            await CleanupAsync(dbContext, [CustomerCode], [slipA, slipB]);
        }
    }

    [Fact]
    public async Task 解除してから同条件で再締めすると同じ金額で確定できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode);

        var slip = "__TSTREL_ROUNDTRIP";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slip, 1, new DateOnly(2025, 7, 1), quantity: 3m, unitPrice: 1234m));
        await dbContext.SaveChangesAsync();

        try
        {
            var firstResults = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 16));
            var first = Assert.Single(firstResults, r => r.CustomerCode == CustomerCode);
            var firstBillingNumber = first.BillingNumber!;

            await releaseService.ReleaseByBillingDateAsync(new DateOnly(2025, 7, 16));

            var secondResults = await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 16));
            var second = Assert.Single(secondResults, r => r.CustomerCode == CustomerCode);

            Assert.Null(second.SkipReason);
            Assert.NotNull(second.BillingNumber);
            Assert.NotEqual(firstBillingNumber, second.BillingNumber); // 再締めでは新番号を採番する

            Assert.Equal(first.PreviousBalance, second.PreviousBalance);
            Assert.Equal(first.SalesAmount, second.SalesAmount);
            Assert.Equal(first.TaxAmount, second.TaxAmount);
            Assert.Equal(first.CurrentBillingAmount, second.CurrentBillingAmount);

            var persistedSales = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slip);
            Assert.Equal(second.BillingNumber, persistedSales.BillingNumber);
        }
        finally
        {
            await CleanupAsync(dbContext, [CustomerCode], [slip]);
        }
    }

    [Fact]
    public async Task 対象の一部が締め順序逆転で解除できない場合は全体を中止し何も変更しない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, closingService, releaseService) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerCode, TestClosingDay);
        await InsertCustomerAsync(dbContext, CustomerCode2, TestClosingDay2);

        var slipOk = "__TSTREL_AON_OK";
        var slipBlocked = "__TSTREL_AON_BLOCKED";
        var slipNewer = "__TSTREL_AON_NEWER";
        dbContext.Sales.Add(NewSalesLine(CustomerCode, slipOk, 1, new DateOnly(2025, 7, 1), quantity: 2m, unitPrice: 1000m));
        dbContext.Sales.Add(NewSalesLine(CustomerCode2, slipBlocked, 1, new DateOnly(2025, 7, 1), quantity: 3m, unitPrice: 1000m));
        await dbContext.SaveChangesAsync();

        try
        {
            // 同一請求日（2025/07/16）で2得意先を、別々の締め日区分として確定する
            // （締め日区分を分けるのは、同じ締め日区分だと次段でCustomerCode2だけを
            // 再確定させることができないため）。
            var billingOk = Assert.Single(
                await closingService.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 16))).BillingNumber!;
            var billingBlocked = Assert.Single(
                await closingService.ConfirmAsync(TestClosingDay2, new DateOnly(2025, 7, 16))).BillingNumber!;

            // CustomerCode2 だけ、その後さらに新しい締め年月で確定させ、締め順序の逆転を作る。
            dbContext.Sales.Add(NewSalesLine(CustomerCode2, slipNewer, 1, new DateOnly(2025, 8, 1), quantity: 1m, unitPrice: 500m));
            await dbContext.SaveChangesAsync();
            await closingService.ConfirmAsync(TestClosingDay2, new DateOnly(2025, 8, 16));

            // 2025/07/16の一括解除は、CustomerCode2側が締め順序逆転で解除できないため全体を中止する。
            var ex = await Assert.ThrowsAsync<BillingReleaseException>(
                () => releaseService.ReleaseByBillingDateAsync(new DateOnly(2025, 7, 16)));
            Assert.Contains(billingBlocked, ex.Message);

            // 中止された結果、CustomerCode側（本来は解除可能）も一切変更されていないこと。
            var persistedOk = await dbContext.Billings.AsNoTracking().SingleAsync(b => b.BillingNumber == billingOk);
            Assert.Equal(BillingStatus.Confirmed, persistedOk.BillingStatus);
            var persistedSalesOk = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slipOk);
            Assert.Equal(billingOk, persistedSalesOk.BillingNumber);
        }
        finally
        {
            await CleanupAsync(dbContext, [CustomerCode, CustomerCode2], [slipOk, slipBlocked, slipNewer]);
        }
    }

    private static (BmcsDbContext DbContext, BillingClosingService ClosingService, BillingReleaseService ReleaseService) Resolve(
        AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<BillingClosingService>(),
        scope.ServiceProvider.GetRequiredService<BillingReleaseService>());

    private static async Task InsertCustomerAsync(BmcsDbContext dbContext, string customerCode, byte closingDay = TestClosingDay)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            SalesEmployeeCode = "EMP001",
            ClosingDay = closingDay,
            TaxUnit = TaxUnit.Invoice,
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
            ProductCode = "PRD001",
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
                $"DELETE FROM dbo.billings WHERE customer_code = {customerCode}");
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.customers WHERE customer_code = {customerCode}");
        }
    }
}
