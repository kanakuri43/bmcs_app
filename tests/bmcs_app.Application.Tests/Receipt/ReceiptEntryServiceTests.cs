using bmcs_app.Application.Receipt;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BillingEntity = bmcs_app.Domain.Entities.Billing;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using ReceiptAllocationEntity = bmcs_app.Domain.Entities.ReceiptAllocation;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Receipt;

/// <summary>
/// 入金入力画面（TODO.md 7-2）の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する
/// （docs/architecture.md 16章）。明細行は支払手段の内訳（入金方法＋金額）であり、請求への充当
/// （<see cref="ReceiptAllocationEntity"/>）は保存時に内部で自動計算される
/// （docs/design_document.md 17章、2026-09-15改訂）。
/// </summary>
/// <remarks>
/// <see cref="ReceiptEntryService.SaveNewAsync"/> は自前で<c>BeginTransactionAsync</c>する
/// ため、<c>SettlementServiceTests</c>のような「外側をトランザクションで包む」方式は使えない。
/// 代わりに保存後、テスト末尾で作成した伝票を明示的に論理削除して後始末する
/// （<c>DetailInvoiceServiceTests</c>と同じ方式）。
/// </remarks>
public class ReceiptEntryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string BankAccountCode = "BNK001";

    private static ReceiptLineInput CashLine(decimal amount, string? lineRemarks = null) =>
        new(ReceiptMethod.Cash, null, null, amount, lineRemarks);

    private static ReceiptLineInput BankTransferLine(decimal amount, string? bankAccountCode = BankAccountCode) =>
        new(ReceiptMethod.BankTransfer, bankAccountCode, null, amount, null);

    private static ReceiptLineInput PromissoryNoteLine(decimal amount, DateOnly? billDueDate) =>
        new(ReceiptMethod.PromissoryNote, null, billDueDate, amount, null);

    [Fact]
    public async Task 単一の確定済み請求へ全額入金すると前受行なしで全額充当される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE01";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(dbContext, "__TSTBIL_RCE01", customerCode, 10000m);

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(10000m)]);

            var lines = await ReloadReceiptAsync(dbContext, receiptSlipNumber);
            Assert.Single(lines);
            Assert.Equal(10000m, lines[0].Amount);

            var allocations = await ReloadAllocationAsync(dbContext, receiptSlipNumber);
            Assert.Single(allocations);
            Assert.Equal("__TSTBIL_RCE01", allocations[0].BillingNumber);
            Assert.Equal(10000m, allocations[0].AllocatedAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 複数の確定済み請求にまたがる入金は古い順に充当される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE02";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            // 古い順: A(7/20)→B(8/20)。入金12,000はAを全額(10,000)消化後、Bへ残り2,000のみ充当される。
            await InsertBillingAsync(dbContext, "__TSTBIL_RCE02B", customerCode, 8000m, new DateOnly(2026, 8, 20), "202608");
            await InsertBillingAsync(dbContext, "__TSTBIL_RCE02A", customerCode, 10000m, new DateOnly(2026, 7, 20), "202607");

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(12000m)]);

            var allocations = await ReloadAllocationAsync(dbContext, receiptSlipNumber);
            Assert.Equal(2, allocations.Count);
            var lineA = allocations.Single(a => a.BillingNumber == "__TSTBIL_RCE02A");
            var lineB = allocations.Single(a => a.BillingNumber == "__TSTBIL_RCE02B");
            Assert.Equal(10000m, lineA.AllocatedAmount);
            Assert.Equal(2000m, lineB.AllocatedAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 請求額を超える入金は超過分が前受行として保存される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE03";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(dbContext, "__TSTBIL_RCE03", customerCode, 10000m);

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(13000m)]);

            var allocations = await ReloadAllocationAsync(dbContext, receiptSlipNumber);
            Assert.Equal(2, allocations.Count);
            Assert.Equal(10000m, allocations.Single(a => a.BillingNumber == "__TSTBIL_RCE03").AllocatedAmount);
            var unallocated = allocations.Single(a => a.BillingNumber is null);
            Assert.Equal(3000m, unallocated.AllocatedAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 確定済み請求が無い得意先への入金は全額前受行になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE04";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(5000m)]);

            var allocations = await ReloadAllocationAsync(dbContext, receiptSlipNumber);
            Assert.Single(allocations);
            Assert.Null(allocations[0].BillingNumber);
            Assert.Equal(5000m, allocations[0].AllocatedAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 複数の入金方法を混在させた伝票が行どおりに保存され合計が古い順に充当される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE10";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(dbContext, "__TSTBIL_RCE10", customerCode, 10000m);

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLine(2000m), BankTransferLine(8000m)]);

            var lines = await ReloadReceiptAsync(dbContext, receiptSlipNumber);
            Assert.Equal(2, lines.Count);
            var cashLine = lines.Single(l => l.ReceiptMethod == ReceiptMethod.Cash);
            var transferLine = lines.Single(l => l.ReceiptMethod == ReceiptMethod.BankTransfer);
            Assert.Equal(2000m, cashLine.Amount);
            Assert.Null(cashLine.BankAccountCode);
            Assert.Equal(8000m, transferLine.Amount);
            Assert.Equal(BankAccountCode, transferLine.BankAccountCode);

            var allocations = await ReloadAllocationAsync(dbContext, receiptSlipNumber);
            Assert.Single(allocations);
            Assert.Equal("__TSTBIL_RCE10", allocations[0].BillingNumber);
            Assert.Equal(10000m, allocations[0].AllocatedAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 保存後に消込サービスと連携して売上明細行の消込ステータスが更新される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE05";
        var salesSlipNumber = "__TSTSAL_RCE05";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(dbContext, "__TSTBIL_RCE05", customerCode, 10000m);
            await InsertSalesLineAsync(dbContext, salesSlipNumber, customerCode, "__TSTBIL_RCE05", 10000m);

            await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(10000m)]);

            var salesLine = await dbContext.Sales.AsNoTracking()
                .SingleAsync(s => s.SalesSlipNumber == salesSlipNumber);
            Assert.Equal(SettlementStatus.FullySettled, salesLine.SettlementStatus);
            Assert.Equal(10000m, salesLine.SettledAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode, salesSlipNumber);
        }
    }

    [Fact]
    public async Task 都度得意先はこの画面で入金登録できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE06";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Line);

            await Assert.ThrowsAsync<ReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(1000m)]));
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 存在しない得意先は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (_, service) = Resolve(scope);

        await Assert.ThrowsAsync<ReceiptEntryException>(() => service.SaveNewAsync(
            "__TSTRCE_NOEXIST", new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(1000m)]));
    }

    [Fact]
    public async Task 振込で入金先口座が未指定の場合は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE07";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            await Assert.ThrowsAsync<ReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [BankTransferLine(1000m, bankAccountCode: null)]));
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 手形で手形期日が未指定の場合は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE11";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            await Assert.ThrowsAsync<ReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [PromissoryNoteLine(1000m, billDueDate: null)]));
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 明細行が0件の場合は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE12";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            await Assert.ThrowsAsync<ReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: []));
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 入金額の合計が0以下の場合は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE08";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            await Assert.ThrowsAsync<ReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(0m)]));
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 保存した伝票を入金Noで読み込める()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE09";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(dbContext, "__TSTBIL_RCE09", customerCode, 10000m);

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: "テスト摘要", lines: [CashLine(10000m)]);

            var lines = await service.GetByNumberAsync(receiptSlipNumber);
            Assert.Single(lines);
            Assert.Equal(customerCode, lines[0].CustomerCode);
            Assert.Equal("テスト摘要", lines[0].SlipRemarks);
            Assert.Equal(ReceiptMethod.Cash, lines[0].ReceiptMethod);
            Assert.Equal(10000m, lines[0].Amount);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    private static (BmcsDbContext DbContext, ReceiptEntryService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<ReceiptEntryService>());

    private static Task InsertCustomerAsync(
        BmcsDbContext dbContext, string customerCode, TaxUnit taxUnit = TaxUnit.Invoice)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new CustomerEntity
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = taxUnit == TaxUnit.Line ? (byte)0 : (byte)15,
            TaxUnit = taxUnit,
            RoundingType = RoundingType.Floor,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static Task InsertBillingAsync(
        BmcsDbContext dbContext, string billingNumber, string customerCode, decimal currentBillingAmount,
        DateOnly? billingDate = null, string closingYearMonth = "202607")
    {
        var now = DateTime.Now;
        dbContext.Billings.Add(new BillingEntity
        {
            BillingNumber = billingNumber,
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "テスト用得意先",
            BillingDate = billingDate ?? new DateOnly(2026, 7, 20),
            ClosingYearMonth = closingYearMonth,
            PreviousBalance = 0m,
            ReceiptAmount = 0m,
            SalesAmount = currentBillingAmount,
            TaxAmount = 0m,
            CurrentBillingAmount = currentBillingAmount,
            StandardRateTaxableAmount = 0m,
            StandardRateTaxAmount = 0m,
            ReducedRateTaxableAmount = 0m,
            ReducedRateTaxAmount = 0m,
            TaxExemptAmount = 0m,
            BillingStatus = BillingStatus.Confirmed,
            ConfirmedAt = now,
            ConfirmedBy = "TEST",
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static Task InsertSalesLineAsync(
        BmcsDbContext dbContext, string salesSlipNumber, string customerCode, string billingNumber, decimal amount)
    {
        var now = DateTime.Now;
        dbContext.Sales.Add(new SalesEntity
        {
            SalesSlipNumber = salesSlipNumber,
            LineNumber = 1,
            SlipDate = new DateOnly(2026, 7, 1),
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "テスト用得意先",
            SlipType = SlipType.Sales,
            ProductCode = "PRD001",
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
            BillingStatus = BillingLinkStatus.Billed,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            BillingNumber = billingNumber,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static Task<List<Domain.Entities.Receipt>> ReloadReceiptAsync(
        BmcsDbContext dbContext, string receiptSlipNumber) =>
        dbContext.Receipts.AsNoTracking()
            .Where(r => r.ReceiptSlipNumber == receiptSlipNumber)
            .OrderBy(r => r.LineNumber)
            .ToListAsync();

    private static Task<List<ReceiptAllocationEntity>> ReloadAllocationAsync(
        BmcsDbContext dbContext, string receiptSlipNumber) =>
        dbContext.ReceiptAllocations.AsNoTracking()
            .Where(a => a.ReceiptSlipNumber == receiptSlipNumber)
            .OrderBy(a => a.LineNumber)
            .ToListAsync();

    /// <summary>
    /// 作成したテストデータを後始末する。<see cref="ReceiptEntryService.SaveNewAsync"/>は自前で
    /// トランザクションをコミットするため、<c>SettlementServiceTests</c>のような外側Rollback方式は
    /// 使えず、明示的な物理削除が必要（<c>DetailInvoiceServiceTests</c>と同じ方式）。
    /// </summary>
    private static async Task CleanupAsync(
        BmcsDbContext dbContext, string customerCode, string? salesSlipNumber = null)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.receipt_allocation WHERE customer_code = {customerCode}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.receipt WHERE customer_code = {customerCode}");

        if (salesSlipNumber is not null)
        {
            // sales.billing_number が billing を参照するため、billing より先に削除する。
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.sales WHERE sales_slip_number = {salesSlipNumber}");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.billing WHERE customer_code = {customerCode}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.customer WHERE customer_code = {customerCode}");
    }
}
