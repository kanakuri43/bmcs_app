using bmcs_app.Application.Sales;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Sales;

/// <summary>
/// 伝票検索モーダル用の売上検索（<see cref="SalesQueryService.SearchAsync"/>）の結合テスト。
/// 開発用ライブDB（172.16.3.171）に対して実行し、<see cref="CustomerLedgerQueryServiceTests"/>
/// と同じ「外側をトランザクションで包み、テストの最後に必ずRollbackする」方式でseedデータを
/// 一切破壊しない。
/// </summary>
public class SalesQueryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerCode = "__TSTSLQ1";

    /// <summary>
    /// 旧実装は明細行に Take(1000) を掛けてから伝票単位に集約していたため、1伝票あたりの
    /// 明細行数が多いと古い伝票が取りこぼされていた（55伝票×20行=1100行で、直近1000行の窓に
    /// 収まる伝票は約50件しかない）。修正後は伝票キーをSQL側で先に確定するため、全55伝票が
    /// 取得でき、Take(1000)の境界で分断されていた伝票合計も正確になる。
    /// </summary>
    [Fact]
    public async Task 明細行が1000行を超えても最も古い伝票まで取得できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerCode);

            const int slipCount = 55;
            const int lineCountPerSlip = 20;
            const decimal amountPerLine = 500m;
            var baseDate = new DateOnly(2026, 1, 1);

            var slipNumbers = new List<string>();
            var lines = new List<SalesEntity>();
            for (var i = 0; i < slipCount; i++)
            {
                var slipNumber = $"__TSTSLQ_S{i:D3}";
                slipNumbers.Add(slipNumber);
                var slipDate = baseDate.AddDays(i);
                for (short lineNumber = 1; lineNumber <= lineCountPerSlip; lineNumber++)
                {
                    lines.Add(NewSales(CustomerCode, slipNumber, lineNumber, slipDate, amountPerLine));
                }
            }
            dbContext.Sales.AddRange(lines);
            await dbContext.SaveChangesAsync();

            var results = await service.SearchAsync(CustomerCode);

            Assert.Equal(slipCount, results.Count);
            foreach (var slipNumber in slipNumbers)
            {
                var hit = Assert.Single(results, h => h.SalesSlipNumber == slipNumber);
                Assert.Equal(lineCountPerSlip * amountPerLine, hit.TotalAmount);
            }

            // 新しい順（日付降順→伝票番号降順）で返ること。
            Assert.Equal(slipNumbers[^1], results[0].SalesSlipNumber);
            Assert.Equal(slipNumbers[0], results[^1].SalesSlipNumber);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 伝票番号得意先コード得意先名のいずれでも検索できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerCode);
            dbContext.Sales.Add(NewSales(CustomerCode, "__TSTSLQ_KW1", 1, new DateOnly(2026, 8, 1), 1_000m));
            await dbContext.SaveChangesAsync();

            var byNumber = await service.SearchAsync("__TSTSLQ_KW1");
            Assert.Single(byNumber, h => h.SalesSlipNumber == "__TSTSLQ_KW1");

            var byCustomerCode = await service.SearchAsync(CustomerCode);
            Assert.Contains(byCustomerCode, h => h.SalesSlipNumber == "__TSTSLQ_KW1");

            var byCustomerName = await service.SearchAsync("テスト用得意先");
            Assert.Contains(byCustomerName, h => h.SalesSlipNumber == "__TSTSLQ_KW1");

            var byUnrelatedKeyword = await service.SearchAsync("__NOMATCH_KEYWORD__");
            Assert.DoesNotContain(byUnrelatedKeyword, h => h.SalesSlipNumber == "__TSTSLQ_KW1");
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 論理削除された伝票は検索結果に含まれない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerCode);
            var deleted = NewSales(CustomerCode, "__TSTSLQ_DEL1", 1, new DateOnly(2026, 8, 1), 1_000m);
            deleted.IsDeleted = true;
            dbContext.Sales.Add(deleted);
            await dbContext.SaveChangesAsync();

            var results = await service.SearchAsync("__TSTSLQ_DEL1");

            Assert.DoesNotContain(results, h => h.SalesSlipNumber == "__TSTSLQ_DEL1");
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static (BmcsDbContext DbContext, SalesQueryService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<SalesQueryService>());

    private static Task InsertCustomerAsync(BmcsDbContext dbContext, string customerCode)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new CustomerEntity
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = 20,
            TaxUnit = TaxUnit.Invoice,
            RoundingType = RoundingType.Floor,
            BillingCustomerCode = customerCode,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static SalesEntity NewSales(
        string customerCode, string slipNumber, short lineNumber, DateOnly slipDate, decimal amount)
    {
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
            ProductCode = "1001",
            ProductName = "テスト用商品",
            Quantity = 1m,
            UnitPrice = amount,
            Amount = amount,
            CostPrice = 0m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            SlipTaxAmount = null,
            TaxAmount = null,
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
