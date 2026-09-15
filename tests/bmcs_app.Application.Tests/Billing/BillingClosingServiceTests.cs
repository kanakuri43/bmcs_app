using bmcs_app.Application.Billing;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReceiptEntity = bmcs_app.Domain.Entities.Receipt;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Billing;

/// <summary>
/// 請求締め処理（TODO.md 6-1）の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する
/// （docs/architecture.md 16章）。完了条件「締めを2回実行しても二重計上されない」の実証。
/// </summary>
/// <remarks>
/// <see cref="BillingClosingService.ConfirmAsync"/> は内部で独自に<c>BeginTransactionAsync</c>する
/// ため、<c>SalesServiceTests</c>と同じ「専用のテスト得意先で確定した後、finallyで物理削除する」
/// 方式を採る（ネストした<c>BeginTransactionAsync</c>はEF Coreが例外を投げるため、
/// 外側をトランザクションで包みRollbackする方式は使えない）。
///
/// seedの得意先（CUS001=20日締め／CUS002=末日締め／CUS003=都度）とは重ならない
/// <c>closing_day = 15</c>の専用テスト得意先（<c>__TSTCLS1</c>＝請求単位／<c>__TSTCLS2</c>＝伝票単位）
/// を新設する。「締め日を指定して一括」処理する仕様上、同じ締め日の得意先を全件拾うため、
/// seed得意先と締め日を分離することでseedデータには一切触れずに済む。
/// </remarks>
public class BillingClosingServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerInvoice = "__TSTCLS1";
    private const string CustomerSlip = "__TSTCLS2";
    private const byte TestClosingDay = 15;

    [Fact]
    public async Task 請求単位の得意先は税率ごとに1回だけ丸めた税額で締められる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice, RoundingType.Floor);

        var slip = "__TSTBIL_INV01";
        var lines = new[]
        {
            NewSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, new DateOnly(2025, 7, 1),
                SlipType.Sales, TaxCategory.Standard, 10m, quantity: 5m, unitPrice: 1000m, RoundingType.Floor),
            NewSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 2, new DateOnly(2025, 7, 1),
                SlipType.Sales, TaxCategory.Reduced, 8m, quantity: 2m, unitPrice: 500m, RoundingType.Floor),
        };
        dbContext.Sales.AddRange(lines);
        await dbContext.SaveChangesAsync();

        try
        {
            var results = await service.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));

            var target = Assert.Single(results, r => r.CustomerCode == CustomerInvoice);
            Assert.Null(target.SkipReason);
            Assert.NotNull(target.BillingNumber);

            // 標準10%: 5000×10%=500。軽減8%: 1000×8%=80。合計580。
            Assert.Equal(6000m, target.SalesAmount);
            Assert.Equal(580m, target.TaxAmount);
            Assert.Equal(500m, target.StandardRateTaxAmount);
            Assert.Equal(80m, target.ReducedRateTaxAmount);
            Assert.Equal(0m, target.PreviousBalance);
            Assert.Equal(0m, target.ReceiptAmount);
            Assert.Equal(6580m, target.CurrentBillingAmount);

            var persisted = await dbContext.Sales
                .AsNoTracking()
                .Where(s => s.SalesSlipNumber == slip)
                .ToListAsync();
            Assert.All(persisted, l => Assert.Equal(target.BillingNumber, l.BillingNumber));
            Assert.All(persisted, l => Assert.Equal(BillingLinkStatus.Billed, l.BillingStatus));
        }
        finally
        {
            await CleanupAsync(dbContext, CustomerInvoice, [slip]);
        }
    }

    [Fact]
    public async Task 伝票単位の得意先は伝票ごとの税額を積み上げて締められる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerSlip, TaxUnit.Slip, RoundingType.RoundHalfUp);

        var slip1 = "__TSTBIL_SLP01";
        var slip2 = "__TSTBIL_SLP02";
        var lines = new List<SalesEntity>
        {
            // 3702×10%=370.2→四捨五入で370
            NewSalesLine(CustomerSlip, TaxUnit.Slip, slip1, 1, new DateOnly(2025, 7, 2),
                SlipType.Sales, TaxCategory.Standard, 10m, quantity: 3m, unitPrice: 1234m, RoundingType.RoundHalfUp),
            // 500×8%=40
            NewSalesLine(CustomerSlip, TaxUnit.Slip, slip2, 1, new DateOnly(2025, 7, 3),
                SlipType.Sales, TaxCategory.Reduced, 8m, quantity: 1m, unitPrice: 500m, RoundingType.RoundHalfUp),
        };
        AssignSlipTaxAmounts(lines, RoundingType.RoundHalfUp);
        dbContext.Sales.AddRange(lines);
        await dbContext.SaveChangesAsync();

        try
        {
            var results = await service.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));

            var target = Assert.Single(results, r => r.CustomerCode == CustomerSlip);
            Assert.Null(target.SkipReason);
            Assert.Equal(410m, target.TaxAmount); // 370 + 40
            Assert.Equal(370m, target.StandardRateTaxAmount);
            Assert.Equal(40m, target.ReducedRateTaxAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, CustomerSlip, [slip1, slip2]);
        }
    }

    [Fact]
    public async Task 前回残高_二重締め_売上下限なし_入金期間下限_締め順序逆転を一連の締めで確認する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice, RoundingType.Floor);

        var slipA = "__TSTBIL_SEQ_A";
        var slipLate = "__TSTBIL_SEQ_LATE";

        // 期間A（2025年7月・15日締め）: 7/1の売上10000（税抜、標準10%）のみ。
        dbContext.Sales.Add(NewSalesLine(CustomerInvoice, TaxUnit.Invoice, slipA, 1, new DateOnly(2025, 7, 1),
            SlipType.Sales, TaxCategory.Standard, 10m, quantity: 10m, unitPrice: 1000m, RoundingType.Floor));
        await dbContext.SaveChangesAsync();

        try
        {
            var resultsA = await service.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var targetA = Assert.Single(resultsA, r => r.CustomerCode == CustomerInvoice);
            Assert.Null(targetA.SkipReason);
            Assert.Equal(11000m, targetA.CurrentBillingAmount); // 10000 + 1000

            // 二重締め: 同条件で再実行してもスキップされ、金額が二重計上されない（完了条件）。
            var resultsADuplicate = await service.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var targetADuplicate = Assert.Single(resultsADuplicate, r => r.CustomerCode == CustomerInvoice);
            Assert.NotNull(targetADuplicate.SkipReason);
            Assert.Null(targetADuplicate.BillingNumber);

            var slipALine = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slipA);
            Assert.Equal(targetA.BillingNumber, slipALine.BillingNumber); // 上書きされていない

            // 期間A確定後に、期間A締め日より前の日付（7/5）で後から売上を登録する（売上には下限がない）。
            dbContext.Sales.Add(NewSalesLine(CustomerInvoice, TaxUnit.Invoice, slipLate, 1, new DateOnly(2025, 7, 5),
                SlipType.Sales, TaxCategory.Standard, 10m, quantity: 1m, unitPrice: 2000m, RoundingType.Floor));

            // 期間A締め日（7/15）以前の日付（7/10）で後から入金を登録する（入金には下限があり、
            // 期間Bには入らない＝残存リスクとしてどの期間にも入らない）。
            var receiptStale = "__TSTBIL_SEQ_RCP_OLD";
            dbContext.Receipts.Add(NewReceiptLine(
                CustomerInvoice, TaxUnit.Invoice, receiptStale, 1, new DateOnly(2025, 7, 10), 500m));

            // 期間A締め日より後・期間B締め日以前の入金（7/20）は期間Bに正しく入る。
            var receiptInPeriodB = "__TSTBIL_SEQ_RCP_B";
            dbContext.Receipts.Add(NewReceiptLine(
                CustomerInvoice, TaxUnit.Invoice, receiptInPeriodB, 1, new DateOnly(2025, 7, 20), 3000m));

            await dbContext.SaveChangesAsync();

            var resultsB = await service.ConfirmAsync(TestClosingDay, new DateOnly(2025, 8, 15));
            var targetB = Assert.Single(resultsB, r => r.CustomerCode == CustomerInvoice);
            Assert.Null(targetB.SkipReason);
            Assert.Equal(11000m, targetB.PreviousBalance); // 前回（期間A）の請求額を引き継ぐ
            Assert.Equal(2000m, targetB.SalesAmount); // 後から登録した7/5の売上のみ（売上に下限なし）
            Assert.Equal(3000m, targetB.ReceiptAmount); // 7/20の入金のみ（7/10の入金は期間下限より前で対象外）
            Assert.Equal(11000m - 3000m + 2000m + 200m, targetB.CurrentBillingAmount);

            // 締め順序の逆転: 期間B確定後に期間Aを再度締めようとすると拒否される
            // （latestConfirmedが期間Bになり、期間Bの締め年月のほうが新しいため）。
            var resultsAAfterB = await service.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var targetAAfterB = Assert.Single(resultsAAfterB, r => r.CustomerCode == CustomerInvoice);
            Assert.NotNull(targetAAfterB.SkipReason);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.receipt WHERE customer_code = {CustomerInvoice}");
            await CleanupAsync(dbContext, CustomerInvoice, [slipA, slipLate]);
        }
    }

    [Fact]
    public async Task 対象データがなければ前回残高がゼロなら請求データを作らずゼロでなければ繰越請求を作る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerSlip, TaxUnit.Slip, RoundingType.Floor);

        var slip1 = "__TSTBIL_CARRY01";
        var lines = new List<SalesEntity>
        {
            NewSalesLine(CustomerSlip, TaxUnit.Slip, slip1, 1, new DateOnly(2025, 7, 1),
                SlipType.Sales, TaxCategory.Standard, 10m, quantity: 10m, unitPrice: 1000m, RoundingType.Floor),
        };
        AssignSlipTaxAmounts(lines, RoundingType.Floor);
        dbContext.Sales.AddRange(lines);
        await dbContext.SaveChangesAsync();

        try
        {
            // 期間1: 売上10000＋税1000＝11000。
            var results1 = await service.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var target1 = Assert.Single(results1, r => r.CustomerCode == CustomerSlip);
            Assert.Equal(11000m, target1.CurrentBillingAmount);

            // 期間2: 対象売上・入金は無いが前回残高が残っているため、繰越請求として billing を作る。
            var results2 = await service.ConfirmAsync(TestClosingDay, new DateOnly(2025, 8, 15));
            var target2 = Assert.Single(results2, r => r.CustomerCode == CustomerSlip);
            Assert.Null(target2.SkipReason);
            Assert.NotNull(target2.BillingNumber);
            Assert.Equal(11000m, target2.PreviousBalance);
            Assert.Equal(0m, target2.SalesAmount);
            Assert.Equal(11000m, target2.CurrentBillingAmount);

            // 期間2締め日以前・期間1締め日より後の入金で残高をちょうどゼロにする。
            dbContext.Receipts.Add(NewReceiptLine(
                CustomerSlip, TaxUnit.Slip, "__TSTBIL_CARRY_RCP", 1, new DateOnly(2025, 9, 1), 11000m));
            await dbContext.SaveChangesAsync();

            var results3 = await service.ConfirmAsync(TestClosingDay, new DateOnly(2025, 9, 15));
            var target3 = Assert.Single(results3, r => r.CustomerCode == CustomerSlip);
            Assert.Null(target3.SkipReason);
            Assert.Equal(0m, target3.CurrentBillingAmount);

            // 期間4: 完全に対象データが無く、前回残高もゼロ → 請求データを作らない。
            var results4 = await service.ConfirmAsync(TestClosingDay, new DateOnly(2025, 10, 15));
            var target4 = Assert.Single(results4, r => r.CustomerCode == CustomerSlip);
            Assert.NotNull(target4.SkipReason);
            Assert.Null(target4.BillingNumber);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.receipt WHERE customer_code = {CustomerSlip}");
            await CleanupAsync(dbContext, CustomerSlip, [slip1]);
        }
    }

    [Fact]
    public async Task 返品行を含む売上は税額と請求額が相殺される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice, RoundingType.Floor);

        var slip = "__TSTBIL_RETURN01";
        var lines = new[]
        {
            NewSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, new DateOnly(2025, 7, 1),
                SlipType.Sales, TaxCategory.Standard, 10m, quantity: 5m, unitPrice: 1000m, RoundingType.Floor),
            NewSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 2, new DateOnly(2025, 7, 2),
                SlipType.Return, TaxCategory.Standard, 10m, quantity: -5m, unitPrice: 1000m, RoundingType.Floor),
        };
        dbContext.Sales.AddRange(lines);
        await dbContext.SaveChangesAsync();

        try
        {
            var results = await service.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var target = Assert.Single(results, r => r.CustomerCode == CustomerInvoice);

            Assert.Equal(0m, target.SalesAmount);
            Assert.Equal(0m, target.TaxAmount);
            Assert.Equal(0m, target.CurrentBillingAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, CustomerInvoice, [slip]);
        }
    }

    [Fact]
    public async Task 指定した締め日以外の得意先は対象に含まれない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        var results = await service.PreviewAsync(TestClosingDay, new DateOnly(2025, 7, 15));

        Assert.DoesNotContain(results, r => r.CustomerCode is "CUS001" or "CUS002" or "CUS003");
    }

    [Fact]
    public async Task 入金伝票の複数明細行は支払手段ごとの内訳でありreceipt_amountは合計で計上される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);

        await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice, RoundingType.Floor);

        var receiptSlip = "__TSTBIL_RCP_MULTI";
        var now = DateTime.Now;
        // 1件の入金伝票が現金3,000＋振込2,000の2明細行（支払手段の内訳）に分かれるケース。
        // receiptの明細行はもう「伝票単位の値の複写」ではなく行ごとの実額のため、単純にSUMする。
        dbContext.Receipts.AddRange(
            new ReceiptEntity
            {
                ReceiptSlipNumber = receiptSlip,
                LineNumber = 1,
                ReceiptDate = new DateOnly(2025, 7, 5),
                CustomerCode = CustomerInvoice,
                TaxUnit = TaxUnit.Invoice,
                CustomerName = "テスト用得意先",
                ReceiptMethod = ReceiptMethod.Cash,
                Amount = 3000m,
                AllocationStatus = AllocationStatus.PartiallyAllocated,
                CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
            },
            new ReceiptEntity
            {
                ReceiptSlipNumber = receiptSlip,
                LineNumber = 2,
                ReceiptDate = new DateOnly(2025, 7, 5),
                CustomerCode = CustomerInvoice,
                TaxUnit = TaxUnit.Invoice,
                CustomerName = "テスト用得意先",
                ReceiptMethod = ReceiptMethod.BankTransfer,
                BankAccountCode = "BNK001",
                Amount = 2000m,
                AllocationStatus = AllocationStatus.PartiallyAllocated,
                CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
            });
        await dbContext.SaveChangesAsync();

        try
        {
            var results = await service.ConfirmAsync(TestClosingDay, new DateOnly(2025, 7, 15));
            var target = Assert.Single(results, r => r.CustomerCode == CustomerInvoice);

            Assert.Equal(5000m, target.ReceiptAmount); // 3000+2000の単純合計
            Assert.Equal(-5000m, target.CurrentBillingAmount); // 前受金として残高がマイナスになる
        }
        finally
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.receipt WHERE receipt_slip_number = {receiptSlip}");
            await CleanupAsync(dbContext, CustomerInvoice, []);
        }
    }

    private static (BmcsDbContext DbContext, BillingClosingService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<BillingClosingService>());

    private static async Task InsertCustomerAsync(
        BmcsDbContext dbContext, string customerCode, TaxUnit taxUnit, RoundingType roundingType)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            SalesEmployeeCode = "EMP001",
            ClosingDay = TestClosingDay,
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

    private static SalesEntity NewSalesLine(
        string customerCode, TaxUnit taxUnit, string slipNumber, short lineNumber, DateOnly slipDate,
        SlipType slipType, TaxCategory taxCategory, decimal taxRate, decimal quantity, decimal unitPrice,
        RoundingType roundingType)
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
            SlipType = slipType,
            ProductCode = "PRD001",
            ProductName = "テスト用商品",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = amount,
            CostPrice = slipType == SlipType.Discount ? 0m : 700m,
            TaxCategory = taxCategory,
            TaxRate = taxRate,
            SlipTaxAmount = null, // TaxUnit.Slip の場合は AssignSlipTaxAmounts が後で設定する
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

    /// <summary>
    /// <see cref="TaxUnit.Slip"/>（伝票単位）の得意先は、伝票全体で1回だけ丸めた税額を
    /// 全行に持たせる必要がある（<c>CK_sales_tax_amount_by_tax_unit</c>）。本番は
    /// <c>SalesTaxAmountAssigner</c>が担うが、本テストは直接INSERTするため同じ計算を再現する。
    /// </summary>
    private static void AssignSlipTaxAmounts(IEnumerable<SalesEntity> lines, RoundingType roundingType)
    {
        foreach (var group in lines.GroupBy(l => l.SalesSlipNumber))
        {
            var taxLines = group.Select(l => new TaxLine(l.TaxCategory, l.TaxRate, l.Amount));
            var tax = ConsumptionTaxCalculator.CalculateSlipTaxAmount(taxLines, roundingType);
            foreach (var line in group)
            {
                line.SlipTaxAmount = tax;
            }
        }
    }

    private static ReceiptEntity NewReceiptLine(
        string customerCode, TaxUnit taxUnit, string receiptSlipNumber, short lineNumber,
        DateOnly receiptDate, decimal amount)
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
            ReceiptMethod = ReceiptMethod.BankTransfer,
            BankAccountCode = "BNK001",
            Amount = amount,
            AllocationStatus = AllocationStatus.Unallocated,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// テスト用得意先とその締め処理が生成した行（sales／billing）をFKの順に物理削除する。
    /// receiptは呼び出し元がテストごとに個別のWHEREで削除する（相乗りする伝票番号が
    /// テストごとに異なるため）。
    /// </summary>
    private static async Task CleanupAsync(
        BmcsDbContext dbContext, string customerCode, IReadOnlyList<string> salesSlipNumbers)
    {
        foreach (var slipNumber in salesSlipNumbers)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.sales WHERE sales_slip_number = {slipNumber}");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.billing WHERE customer_code = {customerCode}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.customer WHERE customer_code = {customerCode}");
    }
}
