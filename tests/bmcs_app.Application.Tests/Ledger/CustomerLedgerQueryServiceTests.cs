using bmcs_app.Application.Ledger;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BillingEntity = bmcs_app.Domain.Entities.Billing;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using DetailInvoiceEntity = bmcs_app.Domain.Entities.DetailInvoice;
using DetailInvoiceSalesLineEntity = bmcs_app.Domain.Entities.DetailInvoiceSalesLine;
using DetailReceiptEntity = bmcs_app.Domain.Entities.DetailReceipt;
using ReceiptEntity = bmcs_app.Domain.Entities.Receipt;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Ledger;

/// <summary>
/// 得意先元帳の照会（TODO.md 8-1）の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する。
/// <see cref="SettlementServiceTests"/> と同じ「外側をトランザクションで包み、テストの最後に
/// 必ずRollbackする」方式で seed データを一切破壊しない。
/// </summary>
public class CustomerLedgerQueryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private static readonly DateOnly PeriodFrom = new(2026, 7, 1);
    private static readonly DateOnly PeriodTo = new(2026, 8, 31);

    private const string CustomerInvoice = "__TSTLED1"; // 請求単位
    private const string CustomerSlip = "__TSTLED2"; // 伝票単位
    private const string CustomerLine = "__TSTLED3"; // 内税明細単位（都度得意先）

    [Fact]
    public async Task 請求単位の得意先の残高推移が実DB経由でも手計算と一致する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice, RoundingType.Floor);
            await InsertBillingAsync(dbContext, "__TSTLEDBIL1", CustomerInvoice, new DateOnly(2026, 7, 20), taxAmount: 1_000m);
            await InsertSalesAsync(dbContext,
                NewClosingSales(CustomerInvoice, TaxUnit.Invoice, "__TSTLEDSAL1", 1, new DateOnly(2026, 7, 15), 10_000m,
                    billingNumber: "__TSTLEDBIL1"),
                NewClosingSales(CustomerInvoice, TaxUnit.Invoice, "__TSTLEDSAL2", 1, new DateOnly(2026, 8, 10), 5_000m,
                    billingNumber: null));
            await InsertReceiptAsync(dbContext,
                NewReceipt(CustomerInvoice, TaxUnit.Invoice, "__TSTLEDRCP1", 1, new DateOnly(2026, 7, 25), 4_000m),
                NewReceipt(CustomerInvoice, TaxUnit.Invoice, "__TSTLEDRCP2", 1, new DateOnly(2026, 8, 5), 3_000m));

            var result = await service.GetAsync(CustomerInvoice, PeriodFrom, PeriodTo);

            Assert.NotNull(result);
            Assert.Equal(9_500m, result.ClosingBalance);
            Assert.True(result.IsBalanced);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    /// <summary>
    /// TODO.md 8-2「リアルタイム残高の常時表示」の完了条件「伝票登録直後に残高が正しく変わる」の
    /// 直接検証。残高キャッシュ列を持たないため（M-11）、登録直後に呼び出すだけで最新値になる。
    /// </summary>
    [Fact]
    public async Task 現在残高は伝票登録直後に反映される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice, RoundingType.Floor);
            var today = DateOnly.FromDateTime(DateTime.Today);

            var before = await service.GetBalanceAsOfAsync(CustomerInvoice, today);
            Assert.Equal(0m, before);

            await InsertSalesAsync(dbContext,
                NewClosingSales(CustomerInvoice, TaxUnit.Invoice, "__TSTLEDSALNOW", 1, today, 3_000m, billingNumber: null));

            // 未請求分は仮計算消費税（切捨・10%）も残高に含まれる。
            var after = await service.GetBalanceAsOfAsync(CustomerInvoice, today);
            Assert.Equal(3_300m, after);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 解除済みの請求データは残高計算から除外される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice, RoundingType.Floor);
            var released = NewClosingBilling("__TSTLEDBILR", CustomerInvoice, new DateOnly(2026, 7, 20), taxAmount: 1_000m);
            released.BillingStatus = BillingStatus.Released;
            released.ReleasedAt = DateTime.Now;
            released.ReleasedBy = "TEST";
            dbContext.Billings.Add(released);
            await dbContext.SaveChangesAsync();

            // 解除済みなので、紐づく売上行は billing_number = null（未請求）に戻っている前提。
            await InsertSalesAsync(dbContext,
                NewClosingSales(CustomerInvoice, TaxUnit.Invoice, "__TSTLEDSALR", 1, new DateOnly(2026, 7, 15), 10_000m,
                    billingNumber: null));

            var result = await service.GetAsync(CustomerInvoice, PeriodFrom, PeriodTo);

            Assert.NotNull(result);
            // 解除済み請求の税額(1,000)は計上されず、代わりに未締め区間の仮計算税(1,000)が計上される
            // ため、残高自体は変わらない。ここでは「解除済みの確定税額行」が現れないことを検証する。
            Assert.DoesNotContain(result.Entries, e => e.BillingNumber == "__TSTLEDBILR");
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 論理削除済みの売上と入金は除外される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerSlip, TaxUnit.Slip, RoundingType.RoundHalfUp);
            var deletedSales = NewClosingSales(CustomerSlip, TaxUnit.Slip, "__TSTLEDSALD", 1, new DateOnly(2026, 7, 10), 1_000m, billingNumber: null);
            deletedSales.SlipTaxAmount = 100m;
            deletedSales.IsDeleted = true;
            dbContext.Sales.Add(deletedSales);

            var deletedReceipt = NewReceipt(CustomerSlip, TaxUnit.Slip, "__TSTLEDRCPD", 1, new DateOnly(2026, 7, 20), 500m);
            deletedReceipt.IsDeleted = true;
            dbContext.Receipts.Add(deletedReceipt);
            await dbContext.SaveChangesAsync();

            var result = await service.GetAsync(CustomerSlip, PeriodFrom, PeriodTo);

            Assert.NotNull(result);
            Assert.Equal(0m, result.ClosingBalance);
            Assert.Single(result.Entries); // 繰越行のみ
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 都度得意先の明細請求書経由の入金は連携する売上明細行に証跡として反映される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerLine, TaxUnit.Line, RoundingType.Ceiling);
            await InsertSalesAsync(dbContext,
                NewLineSales(CustomerLine, "__TSTLEDSALL1", 1, new DateOnly(2026, 7, 20), 8_250m));
            await InsertDetailInvoiceAsync(dbContext, "__TSTLEDDIV1", CustomerLine);
            await InsertDetailReceiptAsync(dbContext,
                NewInvoiceDetailReceipt(CustomerLine, "__TSTLEDDRC1", 1, new DateOnly(2026, 7, 22), 8_250m, "__TSTLEDDIV1"));
            await InsertDetailInvoiceSalesLineAsync(dbContext, "__TSTLEDDIV1", "__TSTLEDSALL1", 1);

            var result = await service.GetAsync(CustomerLine, PeriodFrom, PeriodTo);

            Assert.NotNull(result);
            Assert.Equal(0m, result.ClosingBalance);
            var salesEntry = result.Entries.Single(e => e.SalesSlipNumber == "__TSTLEDSALL1");
            Assert.Equal("__TSTLEDDRC1", salesEntry.ReceiptSlipNumber);
            Assert.Null(salesEntry.ReceiptAmount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 存在しない得意先はnullを返す()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (_, service) = Resolve(scope);

        var result = await service.GetAsync("__TSTLED_NONE", PeriodFrom, PeriodTo);

        Assert.Null(result);
    }

    /// <summary>
    /// scripts/seed_dev_data.sql の3得意先（CUS001/CUS002/CUS003）を読むだけの回帰検知テスト。
    /// トランザクション不要（書き込みを行わない）。seed データが変わった場合はこのテストが
    /// 落ちることで気づけるようにする。期待値は docs/design_document.md 21章の手計算による。
    /// </summary>
    [Theory]
    [InlineData("CUS001", 9_500)]
    [InlineData("CUS002", 2_100)]
    [InlineData("CUS003", 9_900)]
    public async Task seedデータ3得意先の残高が手計算と一致する(string customerCode, decimal expectedClosingBalance)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<CustomerLedgerQueryService>();

        var result = await service.GetAsync(customerCode, PeriodFrom, PeriodTo);

        Assert.NotNull(result);
        Assert.Equal(expectedClosingBalance, result.ClosingBalance);
        Assert.True(result.IsBalanced);
    }

    private static (BmcsDbContext DbContext, CustomerLedgerQueryService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<CustomerLedgerQueryService>());

    private static Task InsertCustomerAsync(BmcsDbContext dbContext, string customerCode, TaxUnit taxUnit, RoundingType roundingType)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new CustomerEntity
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = taxUnit == TaxUnit.Line ? (byte)0 : (byte)20,
            TaxUnit = taxUnit,
            RoundingType = roundingType,
            BillingCustomerCode = customerCode,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static BillingEntity NewClosingBilling(
        string billingNumber, string customerCode, DateOnly billingDate, decimal taxAmount) => new()
    {
        BillingNumber = billingNumber,
        CustomerCode = customerCode,
        TaxUnit = TaxUnit.Invoice,
        CustomerName = "テスト用得意先",
        BillingDate = billingDate,
        ClosingYearMonth = billingDate.ToString("yyyyMM"),
        PreviousBalance = 0m,
        ReceiptAmount = 0m,
        SalesAmount = 0m,
        TaxAmount = taxAmount,
        CurrentBillingAmount = 0m,
        StandardRateTaxableAmount = 0m,
        StandardRateTaxAmount = 0m,
        ReducedRateTaxableAmount = 0m,
        ReducedRateTaxAmount = 0m,
        TaxExemptAmount = 0m,
        BillingStatus = BillingStatus.Confirmed,
        ConfirmedAt = DateTime.Now,
        ConfirmedBy = "TEST",
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static Task InsertBillingAsync(
        BmcsDbContext dbContext, string billingNumber, string customerCode, DateOnly billingDate, decimal taxAmount)
    {
        dbContext.Billings.Add(NewClosingBilling(billingNumber, customerCode, billingDate, taxAmount));
        return dbContext.SaveChangesAsync();
    }

    private static Task InsertSalesAsync(BmcsDbContext dbContext, params SalesEntity[] lines)
    {
        dbContext.Sales.AddRange(lines);
        return dbContext.SaveChangesAsync();
    }

    private static Task InsertReceiptAsync(BmcsDbContext dbContext, params ReceiptEntity[] lines)
    {
        dbContext.Receipts.AddRange(lines);
        return dbContext.SaveChangesAsync();
    }

    private static Task InsertDetailReceiptAsync(BmcsDbContext dbContext, params DetailReceiptEntity[] lines)
    {
        dbContext.DetailReceipts.AddRange(lines);
        return dbContext.SaveChangesAsync();
    }

    private static Task InsertDetailInvoiceAsync(BmcsDbContext dbContext, string detailInvoiceNumber, string customerCode)
    {
        var now = DateTime.Now;
        dbContext.DetailInvoices.Add(new DetailInvoiceEntity
        {
            DetailInvoiceNumber = detailInvoiceNumber,
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            AddresseeName = "テスト用宛名",
            IssueDate = new DateOnly(2026, 7, 20),
            SalesAmount = 0m,
            TaxAmount = 0m,
            TotalAmount = 0m,
            StandardRateTaxableAmount = 0m,
            StandardRateTaxAmount = 0m,
            ReducedRateTaxableAmount = 0m,
            ReducedRateTaxAmount = 0m,
            TaxExemptAmount = 0m,
            InvoiceStatus = DetailInvoiceStatus.Issued,
            IssuedAt = now,
            IssuedBy = "TEST",
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static Task InsertDetailInvoiceSalesLineAsync(
        BmcsDbContext dbContext, string detailInvoiceNumber, string salesSlipNumber, short salesLineNumber)
    {
        var now = DateTime.Now;
        dbContext.DetailInvoiceSalesLines.Add(new DetailInvoiceSalesLineEntity
        {
            DetailInvoiceNumber = detailInvoiceNumber,
            SalesSlipNumber = salesSlipNumber,
            SalesLineNumber = salesLineNumber,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static SalesEntity NewClosingSales(
        string customerCode, TaxUnit taxUnit, string slipNumber, short lineNumber, DateOnly slipDate,
        decimal amount, string? billingNumber)
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
            BillingStatus = billingNumber is not null ? BillingLinkStatus.Billed : BillingLinkStatus.Unbilled,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            BillingNumber = billingNumber,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    private static SalesEntity NewLineSales(
        string customerCode, string slipNumber, short lineNumber, DateOnly slipDate, decimal amount)
    {
        var now = DateTime.Now;
        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = lineNumber,
            SlipDate = slipDate,
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Line,
            CustomerName = "テスト用得意先",
            SlipType = SlipType.Sales,
            ProductCode = "PRD002",
            ProductName = "テスト用商品",
            Quantity = 1m,
            UnitPrice = amount,
            Amount = amount,
            CostPrice = 0m,
            TaxCategory = TaxCategory.Reduced,
            TaxRate = 8m,
            SlipTaxAmount = null,
            TaxAmount = 0m,
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

    private static ReceiptEntity NewReceipt(
        string customerCode, TaxUnit taxUnit, string receiptSlipNumber, short lineNumber, DateOnly receiptDate, decimal amount)
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
            BankAccountCode = "BNK001",
            Amount = amount,
            AllocationStatus = AllocationStatus.Unallocated,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    private static DetailReceiptEntity NewInvoiceDetailReceipt(
        string customerCode, string detailReceiptNumber, short lineNumber, DateOnly receiptDate,
        decimal amount, string targetDetailInvoiceNumber)
    {
        var now = DateTime.Now;
        return new DetailReceiptEntity
        {
            DetailReceiptNumber = detailReceiptNumber,
            LineNumber = lineNumber,
            ReceiptDate = receiptDate,
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            DepositMethodCode = "TRANSFER",
            ReceiptAmount = amount,
            TargetType = DetailReceiptTargetType.DetailInvoice,
            TargetSalesSlipNumber = null,
            TargetSalesLineNumber = null,
            TargetDetailInvoiceNumber = targetDetailInvoiceNumber,
            AllocatedAmount = amount,
            FeeAdjustmentAmount = 0m,
            AllocationStatus = AllocationStatus.Unallocated,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }
}
