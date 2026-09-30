using bmcs_app.Application.Receipt;
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
/// 確定後のロック（TODO.md 9-2）の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する。
/// 月次締めの行はテストが直接INSERTし（締め処理自体は <see cref="MonthlyClosingServiceTests"/> の担当）、
/// 専用のテスト得意先（<c>__TSTMLK*</c>）に対してのみ作るため、後始末は得意先コードで物理削除する。
/// </summary>
public class MonthlyClosedLockTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string Root = "__TSTMLK1";
    private const string Child = "__TSTMLK2";
    private const string LineCustomer = "__TSTMLK3";

    private static readonly DateOnly Closed = new(2020, 3, 31);
    private static readonly DateOnly ClosedDay = new(2020, 3, 10);
    private static readonly DateOnly OpenDay = new(2020, 4, 10);

    [Fact]
    public async Task 月次締め済みの月には売上を新規登録できず翌月は登録できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (db, sales, _, _) = Resolve(scope);
        await InsertCustomerAsync(db, Root, TaxUnit.Invoice);
        await InsertClosingAsync(db, Root, Closed);

        try
        {
            var ex = await Assert.ThrowsAsync<SalesOperationException>(
                () => sales.CreateAsync([NewSalesLine(Root, TaxUnit.Invoice, ClosedDay)], RoundingType.Floor));
            Assert.Contains("月次締め", ex.Message);

            var slip = await sales.CreateAsync([NewSalesLine(Root, TaxUnit.Invoice, OpenDay)], RoundingType.Floor);
            Assert.False(string.IsNullOrEmpty(slip));
        }
        finally
        {
            await CleanupAsync(db, Child, Root);
        }
    }

    [Fact]
    public async Task 締め済みの月への日付変更は拒否され既存伝票の訂正と取消も拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (db, sales, _, _) = Resolve(scope);
        await InsertCustomerAsync(db, Root, TaxUnit.Invoice);

        try
        {
            // 未締めの4月と3月に1伝票ずつ登録してから、3月だけ月次締めを確定させる。
            var aprilSlip = await sales.CreateAsync([NewSalesLine(Root, TaxUnit.Invoice, OpenDay)], RoundingType.Floor);
            var marchSlip = await sales.CreateAsync([NewSalesLine(Root, TaxUnit.Invoice, ClosedDay)], RoundingType.Floor);
            await InsertClosingAsync(db, Root, Closed);

            // 4月の伝票の日付を締め済みの3月へ動かす → 訂正後の状態がロック対象なので拒否。
            var moved = NewSalesLine(Root, TaxUnit.Invoice, ClosedDay);
            moved.LineNumber = 1;
            await Assert.ThrowsAsync<SalesOperationException>(
                () => sales.UpdateAsync(aprilSlip, [moved], RoundingType.Floor, [1]));

            // 3月の既存伝票は訂正も取消もできない。
            var edited = NewSalesLine(Root, TaxUnit.Invoice, ClosedDay);
            edited.LineNumber = 1;
            await Assert.ThrowsAsync<SalesOperationException>(
                () => sales.UpdateAsync(marchSlip, [edited], RoundingType.Floor, [1]));
            var ex = await Assert.ThrowsAsync<SalesOperationException>(() => sales.CancelSlipAsync(marchSlip));
            Assert.Contains("月次締め", ex.Message);
        }
        finally
        {
            await CleanupAsync(db, Child, Root);
        }
    }

    [Fact]
    public async Task 請求集約先だけが確定済みでも請求集約元の売上は登録も訂正も取消もできない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (db, sales, _, _) = Resolve(scope);
        await InsertCustomerAsync(db, Root, TaxUnit.Invoice);
        await InsertCustomerAsync(db, Child, TaxUnit.Invoice, billingCustomerCode: Root);

        try
        {
            // 集約元の売上を3月に登録してから、集約先の行だけ確定させる（集約元の行は無い）。
            var childSlip = await sales.CreateAsync([NewSalesLine(Child, TaxUnit.Invoice, ClosedDay)], RoundingType.Floor);
            await InsertClosingAsync(db, Root, Closed);
            Assert.False(await db.MonthlyClosings.AnyAsync(m => m.CustomerCode == Child));

            var ex = await Assert.ThrowsAsync<SalesOperationException>(
                () => sales.CreateAsync([NewSalesLine(Child, TaxUnit.Invoice, ClosedDay)], RoundingType.Floor));
            Assert.Contains("月次締め", ex.Message);

            var edited = NewSalesLine(Child, TaxUnit.Invoice, ClosedDay);
            edited.LineNumber = 1;
            await Assert.ThrowsAsync<SalesOperationException>(
                () => sales.UpdateAsync(childSlip, [edited], RoundingType.Floor, [1]));
            await Assert.ThrowsAsync<SalesOperationException>(() => sales.CancelSlipAsync(childSlip));

            // 翌月は登録できる。
            await sales.CreateAsync([NewSalesLine(Child, TaxUnit.Invoice, OpenDay)], RoundingType.Floor);
        }
        finally
        {
            await CleanupAsync(db, Child, Root);
        }
    }

    [Fact]
    public async Task 解除済みの月は再び登録できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (db, sales, _, _) = Resolve(scope);
        await InsertCustomerAsync(db, Root, TaxUnit.Invoice);
        await InsertClosingAsync(db, Root, Closed, ClosingStatus.Released);

        try
        {
            var slip = await sales.CreateAsync([NewSalesLine(Root, TaxUnit.Invoice, ClosedDay)], RoundingType.Floor);
            Assert.False(string.IsNullOrEmpty(slip));
        }
        finally
        {
            await CleanupAsync(db, Child, Root);
        }
    }

    [Fact]
    public async Task 月次締め済みの月には締め入金を新規登録できず訂正と取消もできない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (db, _, receipts, _) = Resolve(scope);
        await InsertCustomerAsync(db, Root, TaxUnit.Invoice);
        ReceiptLineInput[] lines = [new("CASH", null, null, 1000m, null)];

        try
        {
            // 4月に登録した入金は、3月の月次締めがあっても問題なく登録・取消できる。
            var aprilSlip = await receipts.SaveNewAsync(Root, OpenDay, null, lines);
            await InsertClosingAsync(db, Root, Closed);

            var ex = await Assert.ThrowsAsync<ReceiptEntryException>(
                () => receipts.SaveNewAsync(Root, ClosedDay, null, lines));
            Assert.Contains("月次締め", ex.Message);

            // 締め済みの3月へ日付を動かす訂正は拒否される。
            var loaded = await db.Receipts.AsNoTracking()
                .Where(r => r.ReceiptSlipNumber == aprilSlip).Select(r => r.LineNumber).ToListAsync();
            var correction = new ReceiptLineCorrection(loaded[0], "CASH", null, null, 1000m, null);
            await Assert.ThrowsAsync<ReceiptEntryException>(
                () => receipts.UpdateAsync(aprilSlip, ClosedDay, null, [correction], loaded));

            // 翌月は登録できる。
            await receipts.SaveNewAsync(Root, OpenDay, null, lines);
        }
        finally
        {
            await CleanupAsync(db, Child, Root);
        }
    }

    [Fact]
    public async Task 月次締め済みの月には明細入金を新規登録できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (db, _, _, detailReceipts) = Resolve(scope);
        await InsertCustomerAsync(db, LineCustomer, TaxUnit.Line, closingDay: 0);
        await InsertClosingAsync(db, LineCustomer, Closed, taxUnit: TaxUnit.Line);
        DetailReceiptLineInput[] lines =
            [new(DetailReceiptTargetType.SalesLine, "__NONE", 1, null, "CASH", null, null)];

        try
        {
            var ex = await Assert.ThrowsAsync<DetailReceiptEntryException>(
                () => detailReceipts.SaveNewAsync(LineCustomer, ClosedDay, null, lines));
            Assert.Contains("月次締め", ex.Message);

            // 対照: 未締めの月なら月次締めでは拒否されず、存在しない対象の別の理由で拒否される。
            var other = await Assert.ThrowsAsync<DetailReceiptEntryException>(
                () => detailReceipts.SaveNewAsync(LineCustomer, OpenDay, null, lines));
            Assert.DoesNotContain("月次締め", other.Message);
        }
        finally
        {
            await CleanupAsync(db, Child, Root, LineCustomer);
        }
    }

    private static (BmcsDbContext Db, SalesService Sales, ReceiptEntryService Receipts, DetailReceiptEntryService DetailReceipts)
        Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<SalesService>(),
        scope.ServiceProvider.GetRequiredService<ReceiptEntryService>(),
        scope.ServiceProvider.GetRequiredService<DetailReceiptEntryService>());

    private static async Task InsertCustomerAsync(
        BmcsDbContext db, string customerCode, TaxUnit taxUnit, string? billingCustomerCode = null, byte closingDay = 15)
    {
        var now = DateTime.Now;
        db.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            SalesEmployeeCode = null,
            ClosingDay = closingDay,
            TaxUnit = taxUnit,
            RoundingType = RoundingType.Floor,
            BillingCustomerCode = billingCustomerCode ?? customerCode,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task InsertClosingAsync(
        BmcsDbContext db, string customerCode, DateOnly closingDate,
        ClosingStatus status = ClosingStatus.Confirmed, TaxUnit taxUnit = TaxUnit.Invoice)
    {
        var now = DateTime.Now;
        db.MonthlyClosings.Add(new MonthlyClosing
        {
            ClosingDate = closingDate,
            CustomerCode = customerCode,
            TaxUnit = taxUnit,
            CustomerName = "テスト用得意先",
            PreviousBalance = 0m,
            SalesAmount = 0m,
            ReceiptAmount = 0m,
            TaxAmount = 0m,
            ClosingBalance = 0m,
            StandardRateTaxableAmount = 0m,
            StandardRateTaxAmount = 0m,
            ReducedRateTaxableAmount = 0m,
            ReducedRateTaxAmount = 0m,
            TaxExemptAmount = 0m,
            ClosingStatus = status,
            ConfirmedAt = now,
            ConfirmedBy = "TEST",
            ReleasedAt = status == ClosingStatus.Released ? now : null,
            ReleasedBy = status == ClosingStatus.Released ? "TEST" : null,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static SalesEntity NewSalesLine(string customerCode, TaxUnit taxUnit, DateOnly slipDate)
    {
        var now = DateTime.Now;
        return new SalesEntity
        {
            SalesSlipNumber = string.Empty,
            LineNumber = 1,
            SlipDate = slipDate,
            CustomerCode = customerCode,
            TaxUnit = taxUnit,
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

    /// <summary>テスト得意先が持つ行をFKの順（子→親）に物理削除する。得意先コードは子から順に渡す。</summary>
    private static async Task CleanupAsync(BmcsDbContext db, params string[] customerCodes)
    {
        foreach (var code in customerCodes)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.monthly_closings WHERE customer_code = {code}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.receipt_allocations WHERE receipt_slip_number IN (SELECT receipt_slip_number FROM dbo.receipts WHERE customer_code = {code})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.receipts WHERE customer_code = {code}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.sales WHERE customer_code = {code}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.customers WHERE customer_code = {code}");
        }
    }
}
