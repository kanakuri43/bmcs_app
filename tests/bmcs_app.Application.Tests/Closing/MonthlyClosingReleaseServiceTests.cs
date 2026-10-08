using bmcs_app.Application.Closing;
using bmcs_app.Application.Sales;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Closing;

/// <summary>
/// 月次締め解除処理の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する。
/// <see cref="MonthlyClosingServiceTests"/>と同じく、実データが存在しない2020年の1〜2月だけを扱い、
/// 後始末でその月の <c>monthly_closings</c> を全件物理削除する。
/// </summary>
public class MonthlyClosingReleaseServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerA = "__TSTMRL1";
    private const string CustomerB = "__TSTMRL2";

    private static readonly DateOnly Jan = new(2020, 1, 31);
    private static readonly DateOnly Feb = new(2020, 2, 29);

    [Fact]
    public async Task 解除すると解除済みになり物理削除されず伝票を再び登録できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (db, closing, release, sales) = Resolve(scope);
        await InsertCustomerAsync(db, CustomerA);
        db.Sales.Add(NewSalesLine(CustomerA, "__TSTMRL_S01", new DateOnly(2020, 1, 10)));
        await db.SaveChangesAsync();

        try
        {
            await closing.ConfirmAsync(2020, 1);
            Assert.Contains(await release.PreviewAsync(2020, 1), t => t.CustomerCode == CustomerA && t.BlockReason is null);

            // 確定中は登録できない。
            await Assert.ThrowsAsync<SalesOperationException>(
                () => sales.CreateAsync([NewSalesLine(CustomerA, string.Empty, new DateOnly(2020, 1, 20))], RoundingType.Floor));

            var released = await release.ReleaseAsync(2020, 1);
            Assert.Contains(released, t => t.CustomerCode == CustomerA);

            var row = await db.MonthlyClosings.AsNoTracking()
                .SingleAsync(m => m.ClosingDate == Jan && m.CustomerCode == CustomerA);
            Assert.Equal(ClosingStatus.Released, row.ClosingStatus);
            Assert.NotNull(row.ReleasedAt);
            Assert.False(string.IsNullOrEmpty(row.ReleasedBy));
            Assert.False(row.IsDeleted);

            // 解除後は一覧に出ず、伝票を再び登録できる。
            Assert.DoesNotContain(await release.PreviewAsync(2020, 1), t => t.CustomerCode == CustomerA);
            await sales.CreateAsync([NewSalesLine(CustomerA, string.Empty, new DateOnly(2020, 1, 20))], RoundingType.Floor);

            // 再確定すると解除済みの行が確定に戻り、解除日時・解除者がクリアされる（金額は追加した売上を含む）。
            var reconfirmed = await closing.ConfirmAsync(2020, 1);
            var target = Assert.Single(reconfirmed, r => r.CustomerCode == CustomerA);
            Assert.Null(target.SkipReason);
            Assert.Equal(2000m, target.SalesAmount);
            var again = await db.MonthlyClosings.AsNoTracking()
                .SingleAsync(m => m.ClosingDate == Jan && m.CustomerCode == CustomerA);
            Assert.Equal(ClosingStatus.Confirmed, again.ClosingStatus);
            Assert.Null(again.ReleasedAt);
            Assert.Null(again.ReleasedBy);
        }
        finally
        {
            await CleanupAsync(db, CustomerA, CustomerB);
        }
    }

    [Fact]
    public async Task より後の年月が確定済みの得意先が含まれると何も更新せず中止される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (db, closing, release, _) = Resolve(scope);
        await InsertCustomerAsync(db, CustomerA);
        await InsertCustomerAsync(db, CustomerB);
        // A: 1月に売上（2月に前月残高が残るため2月も確定される）。B: 1月だけ売上があり2月は行が作られる。
        db.Sales.AddRange(
            NewSalesLine(CustomerA, "__TSTMRL_S02", new DateOnly(2020, 1, 10)),
            NewSalesLine(CustomerB, "__TSTMRL_S03", new DateOnly(2020, 1, 11)));
        await db.SaveChangesAsync();

        try
        {
            await closing.ConfirmAsync(2020, 1);
            await closing.ConfirmAsync(2020, 2);

            var preview = await release.PreviewAsync(2020, 1);
            Assert.All(
                preview.Where(t => t.CustomerCode is CustomerA or CustomerB),
                t => Assert.NotNull(t.BlockReason));

            var ex = await Assert.ThrowsAsync<MonthlyClosingException>(() => release.ReleaseAsync(2020, 1));
            Assert.Contains(CustomerA, ex.Message);

            // 何も更新されていない。
            var janRows = await db.MonthlyClosings.AsNoTracking()
                .Where(m => m.ClosingDate == Jan && (m.CustomerCode == CustomerA || m.CustomerCode == CustomerB))
                .ToListAsync();
            Assert.Equal(2, janRows.Count);
            Assert.All(janRows, m => Assert.Equal(ClosingStatus.Confirmed, m.ClosingStatus));

            // 2月を先に解除すれば、1月も解除できる（新しい月から順に解除する運用）。
            await release.ReleaseAsync(2020, 2);
            await release.ReleaseAsync(2020, 1);
            Assert.Equal(0, await db.MonthlyClosings.AsNoTracking()
                .CountAsync(m => (m.CustomerCode == CustomerA || m.CustomerCode == CustomerB)
                    && m.ClosingStatus == ClosingStatus.Confirmed));
        }
        finally
        {
            await CleanupAsync(db, CustomerA, CustomerB);
        }
    }

    [Fact]
    public async Task 確定済みの月次締めが無い年月は解除できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (_, _, release, _) = Resolve(scope);

        Assert.Empty(await release.PreviewAsync(2020, 5));
        await Assert.ThrowsAsync<MonthlyClosingException>(() => release.ReleaseAsync(2020, 5));
    }

    private static (BmcsDbContext Db, MonthlyClosingService Closing, MonthlyClosingReleaseService Release, SalesService Sales)
        Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<MonthlyClosingService>(),
        scope.ServiceProvider.GetRequiredService<MonthlyClosingReleaseService>(),
        scope.ServiceProvider.GetRequiredService<SalesService>());

    private static async Task InsertCustomerAsync(BmcsDbContext db, string customerCode)
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
            BillingCustomerCode = customerCode,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static SalesEntity NewSalesLine(string customerCode, string slipNumber, DateOnly slipDate)
    {
        var now = DateTime.Now;
        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = 1,
            SlipDate = slipDate,
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "テスト用得意先",
            SlipType = SlipType.Sales,
            ProductCode = "1001",
            ProductName = "テスト用商品",
            Quantity = 1m,
            UnitPrice = 1000m,
            Amount = ConsumptionTaxCalculator.CalculateLineAmount(1m, 1000m, RoundingType.Floor),
            CostPrice = 700m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Unbilled,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    /// <summary>2020年1〜2月の月次締めを全件消し、テスト得意先の売上・得意先を物理削除する。</summary>
    private static async Task CleanupAsync(BmcsDbContext db, params string[] customerCodes)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.monthly_closings WHERE closing_date IN ({Jan}, {Feb})");
        foreach (var code in customerCodes)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.sales WHERE customer_code = {code}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.customers WHERE customer_code = {code}");
        }
    }
}
