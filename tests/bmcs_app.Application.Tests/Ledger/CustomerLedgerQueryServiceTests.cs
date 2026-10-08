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
/// 得意先元帳の照会の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する。
/// <see cref="SettlementServiceTests"/> と同じ「外側をトランザクションで包み、テストの最後に
/// 必ずRollbackする」方式で、各テストが自分で作ったデータだけを使い、DBには何も残さない。
/// </summary>
public class CustomerLedgerQueryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private static readonly DateOnly PeriodFrom = new(2026, 7, 1);
    private static readonly DateOnly PeriodTo = new(2026, 8, 31);

    private const string CustomerInvoice = "__TSTLED1"; // 請求単位
    private const string CustomerSlip = "__TSTLED2"; // 伝票単位
    private const string CustomerLine = "__TSTLED3"; // 内税明細単位（都度得意先）
    private const string CustomerAggRoot = "__TSTLED4"; // 請求集約先
    private const string CustomerAggChild = "__TSTLED4C"; // 請求集約元

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
    /// リアルタイム残高表示の要件「伝票登録直後に残高が正しく変わる」の
    /// 直接検証。残高キャッシュ列を持たないため、登録直後に呼び出すだけで最新値になる。
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

    /// <summary>
    /// 請求集約先の元帳はグループ内の請求集約元の売上・入金を合算する。
    /// 請求集約元名義に残った入金（HasBillingChangeLockAsyncがreceiptsを見ないため発生し得る。
    /// docs/design_document.md 28-7章）も含めて残高が正しくなることを検証する。
    /// </summary>
    [Fact]
    public async Task 請求集約先のGetAsyncはグループ内の請求集約元の売上_入金を合算する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerAggRoot, TaxUnit.Invoice, RoundingType.Floor);
            await InsertCustomerAsync(dbContext, CustomerAggChild, TaxUnit.Invoice, RoundingType.Floor, CustomerAggRoot);

            await InsertSalesAsync(dbContext,
                NewClosingSales(CustomerAggRoot, TaxUnit.Invoice, "__TSTLEDSAL4R", 1, new DateOnly(2026, 7, 15), 10_000m, billingNumber: null),
                NewClosingSales(CustomerAggChild, TaxUnit.Invoice, "__TSTLEDSAL4C", 1, new DateOnly(2026, 7, 16), 5_000m, billingNumber: null));

            await InsertReceiptAsync(dbContext,
                NewReceipt(CustomerAggChild, TaxUnit.Invoice, "__TSTLEDRCP4C", 1, new DateOnly(2026, 7, 20), 3_000m));

            var result = await service.GetAsync(CustomerAggRoot, PeriodFrom, PeriodTo);

            Assert.NotNull(result);
            Assert.False(result.IsTransactionHistoryOnly);
            Assert.Equal(15_000m, result.SalesTotal);
            Assert.Equal(3_000m, result.ReceiptTotal);
            // 未締め区間の仮計算税: グループ合算15,000円×10%を切捨→1,500円。
            Assert.Equal(1_500m, result.TaxTotal);
            Assert.Equal(15_000m + 1_500m - 3_000m, result.ClosingBalance);
            Assert.True(result.IsBalanced);
            Assert.Contains(result.Entries, e => e.SalesSlipNumber == "__TSTLEDSAL4C");
            Assert.Contains(result.Entries, e => e.ReceiptSlipNumber == "__TSTLEDRCP4C");
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    /// <summary>請求集約元は取引履歴のみを返し、現在残高もnullになる。</summary>
    [Fact]
    public async Task 請求集約元のGetAsyncは取引履歴のみを返しGetBalanceAsOfAsyncはnullを返す()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerAggRoot, TaxUnit.Invoice, RoundingType.Floor);
            await InsertCustomerAsync(dbContext, CustomerAggChild, TaxUnit.Invoice, RoundingType.Floor, CustomerAggRoot);

            await InsertSalesAsync(dbContext,
                NewClosingSales(CustomerAggChild, TaxUnit.Invoice, "__TSTLEDSAL4C2", 1, new DateOnly(2026, 7, 18), 2_000m, billingNumber: null));

            var result = await service.GetAsync(CustomerAggChild, PeriodFrom, PeriodTo);

            Assert.NotNull(result);
            Assert.True(result.IsTransactionHistoryOnly);
            Assert.Equal(0m, result.ClosingBalance);
            Assert.Equal(2_000m, result.SalesTotal);
            Assert.All(result.Entries, e => Assert.Equal(LedgerEntryKind.Sales, e.Kind));

            var balance = await service.GetBalanceAsOfAsync(CustomerAggChild, PeriodTo);
            Assert.Null(balance);
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
    /// 税単位ごと（請求単位／伝票単位／内税明細単位）に、テスト内で作った売上・入金から
    /// 元帳の期末残高を手計算した値と照合する（金額の手計算一致）。外側のトランザクションを
    /// 必ずRollbackするため、DBには何も残らない。
    /// </summary>
    [Theory]
    [InlineData(TaxUnit.Invoice, 23_600)]
    [InlineData(TaxUnit.Slip, 9_200)]
    [InlineData(TaxUnit.Line, 8_300)]
    public async Task 税単位別の得意先の残高が手計算と一致する(TaxUnit taxUnit, decimal expectedClosingBalance)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            string customerCode;
            switch (taxUnit)
            {
                case TaxUnit.Invoice:
                    // 確定済み請求(売上20,000+税2,000) + 未締め売上6,000(仮計算税600) - 入金5,000 = 23,600。
                    customerCode = CustomerInvoice;
                    await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Invoice, RoundingType.Floor);
                    await InsertBillingAsync(dbContext, "__TSTLEDBILX", customerCode, new DateOnly(2026, 7, 20), taxAmount: 2_000m);
                    await InsertSalesAsync(dbContext,
                        NewClosingSales(customerCode, TaxUnit.Invoice, "__TSTLEDSALX1", 1, new DateOnly(2026, 7, 15), 20_000m,
                            billingNumber: "__TSTLEDBILX"),
                        NewClosingSales(customerCode, TaxUnit.Invoice, "__TSTLEDSALX2", 1, new DateOnly(2026, 8, 10), 6_000m,
                            billingNumber: null));
                    await InsertReceiptAsync(dbContext,
                        NewReceipt(customerCode, TaxUnit.Invoice, "__TSTLEDRCPX1", 1, new DateOnly(2026, 7, 25), 5_000m));
                    break;

                case TaxUnit.Slip:
                    // 伝票A(3,000+2,000, 税500) + 伝票B(7,000, 税700) - 入金4,000 = 9,200。
                    customerCode = CustomerSlip;
                    await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Slip, RoundingType.RoundHalfUp);
                    await InsertSalesAsync(dbContext,
                        NewSlipTaxSales(customerCode, "__TSTLEDSALX3", 1, new DateOnly(2026, 7, 10), 3_000m, slipTaxAmount: 500m),
                        NewSlipTaxSales(customerCode, "__TSTLEDSALX3", 2, new DateOnly(2026, 7, 10), 2_000m, slipTaxAmount: 500m),
                        NewSlipTaxSales(customerCode, "__TSTLEDSALX4", 1, new DateOnly(2026, 8, 11), 7_000m, slipTaxAmount: 700m));
                    await InsertReceiptAsync(dbContext,
                        NewReceipt(customerCode, TaxUnit.Slip, "__TSTLEDRCPX2", 1, new DateOnly(2026, 7, 20), 4_000m));
                    break;

                default:
                    // 内税明細単位: 売上11,000 + 売上3,300（いずれも税込） - 明細入金6,000 = 8,300。
                    customerCode = CustomerLine;
                    await InsertCustomerAsync(dbContext, customerCode, TaxUnit.Line, RoundingType.Ceiling);
                    await InsertSalesAsync(dbContext,
                        NewLineSales(customerCode, "__TSTLEDSALX5", 1, new DateOnly(2026, 7, 20), 11_000m),
                        NewLineSales(customerCode, "__TSTLEDSALX6", 1, new DateOnly(2026, 8, 12), 3_300m));
                    await InsertDetailInvoiceAsync(dbContext, "__TSTLEDDIVX", customerCode);
                    await InsertDetailInvoiceSalesLineAsync(dbContext, "__TSTLEDDIVX", "__TSTLEDSALX5", 1);
                    await InsertDetailReceiptAsync(dbContext,
                        NewInvoiceDetailReceipt(customerCode, "__TSTLEDDRCX", 1, new DateOnly(2026, 7, 22), 6_000m, "__TSTLEDDIVX"));
                    break;
            }

            var result = await service.GetAsync(customerCode, PeriodFrom, PeriodTo);

            Assert.NotNull(result);
            Assert.Equal(expectedClosingBalance, result.ClosingBalance);
            Assert.True(result.IsBalanced);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static (BmcsDbContext DbContext, CustomerLedgerQueryService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<CustomerLedgerQueryService>());

    private static Task InsertCustomerAsync(
        BmcsDbContext dbContext, string customerCode, TaxUnit taxUnit, RoundingType roundingType,
        string? billingCustomerCode = null)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new CustomerEntity
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = taxUnit == TaxUnit.Line ? (byte)0 : (byte)20,
            TaxUnit = taxUnit,
            RoundingType = roundingType,
            BillingCustomerCode = billingCustomerCode ?? customerCode,
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

    private static SalesEntity NewSlipTaxSales(
        string customerCode, string slipNumber, short lineNumber, DateOnly slipDate, decimal amount, decimal slipTaxAmount)
    {
        var sales = NewClosingSales(customerCode, TaxUnit.Slip, slipNumber, lineNumber, slipDate, amount, billingNumber: null);
        sales.SlipTaxAmount = slipTaxAmount;
        return sales;
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
