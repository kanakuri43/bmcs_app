using bmcs_app.Application.Billing;
using bmcs_app.Application.Common;
using bmcs_app.Application.Receipt;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BillingEntity = bmcs_app.Domain.Entities.Billing;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using MonthlyClosingEntity = bmcs_app.Domain.Entities.MonthlyClosing;
using ReceiptAllocationEntity = bmcs_app.Domain.Entities.ReceiptAllocation;
using ReceiptEntity = bmcs_app.Domain.Entities.Receipt;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Receipt;

/// <summary>
/// 入金入力画面（TODO.md 7-2）・入金の取消訂正（TODO.md 7-5）の結合テスト。開発用ライブDB
/// （172.16.3.171）に対して実行する（docs/architecture.md 16章）。明細行は支払手段の内訳
/// （入金方法＋金額）であり、請求への充当（<see cref="ReceiptAllocationEntity"/>）は保存時に
/// 内部で自動計算される（docs/design_document.md 17章、2026-09-15改訂）。
/// </summary>
/// <remarks>
/// <see cref="ReceiptEntryService.SaveNewAsync"/>／<see cref="ReceiptEntryService.UpdateAsync"/>／
/// <see cref="ReceiptEntryService.CancelSlipAsync"/> はいずれも自前で<c>BeginTransactionAsync</c>する
/// ため、<c>SettlementServiceTests</c>のような「外側をトランザクションで包む」方式は使えない。
/// 代わりに保存後、テスト末尾で作成した伝票を明示的に論理削除して後始末する
/// （<c>DetailInvoiceServiceTests</c>と同じ方式）。
/// </remarks>
public class ReceiptEntryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string BankAccountCode = "BNK001";

    private const string CashMethod = "CASH";
    private const string BankTransferMethod = "TRANSFER";
    private const string PromissoryNoteMethod = "NOTE";

    private static ReceiptLineInput CashLine(decimal amount, string? lineRemarks = null) =>
        new(CashMethod, null, null, amount, lineRemarks);

    private static ReceiptLineInput BankTransferLine(decimal amount, string? bankAccountCode = BankAccountCode) =>
        new(BankTransferMethod, bankAccountCode, null, amount, null);

    private static ReceiptLineInput PromissoryNoteLine(decimal amount, DateOnly? billDueDate) =>
        new(PromissoryNoteMethod, null, billDueDate, amount, null);

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
            var cashLine = lines.Single(l => l.DepositMethodCode == CashMethod);
            var transferLine = lines.Single(l => l.DepositMethodCode == BankTransferMethod);
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
    public async Task 存在しない入金方法コードを指定すると例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCE13";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            var ex = await Assert.ThrowsAsync<ReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [new ReceiptLineInput("__NOEXIST", null, null, 1000m, null)]));
            Assert.Contains("見つかりません", ex.Message);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
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
            Assert.Equal(CashMethod, lines[0].DepositMethodCode);
            Assert.Equal(10000m, lines[0].Amount);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    // ── 取消（TODO.md 7-5） ──────────────────────────────────────

    [Fact]
    public async Task 取消すると売上の消込状態と消込済金額が入金前に戻る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCC01";
        var salesSlipNumber = "__TSTSAL_RCC01";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(dbContext, "__TSTBIL_RCC01", customerCode, 10000m);
            await InsertSalesLineAsync(dbContext, salesSlipNumber, customerCode, "__TSTBIL_RCC01", 10000m);

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(10000m)]);

            var settledSalesLine = await dbContext.Sales.AsNoTracking()
                .SingleAsync(s => s.SalesSlipNumber == salesSlipNumber);
            Assert.Equal(SettlementStatus.FullySettled, settledSalesLine.SettlementStatus);

            await service.CancelSlipAsync(receiptSlipNumber);

            var revertedSalesLine = await dbContext.Sales.AsNoTracking()
                .SingleAsync(s => s.SalesSlipNumber == salesSlipNumber);
            Assert.Equal(SettlementStatus.Unsettled, revertedSalesLine.SettlementStatus);
            Assert.Equal(0m, revertedSalesLine.SettledAmount);

            var receiptLines = await ReloadReceiptAsync(dbContext, receiptSlipNumber);
            Assert.All(receiptLines, l => Assert.True(l.IsDeleted));
            var allocations = await ReloadAllocationAsync(dbContext, receiptSlipNumber);
            Assert.All(allocations, a => Assert.True(a.IsDeleted));
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode, salesSlipNumber);
        }
    }

    [Fact]
    public async Task 取消しても他の入金による消込は残る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCC02";
        var salesSlipNumber = "__TSTSAL_RCC02";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(dbContext, "__TSTBIL_RCC02", customerCode, 10000m);
            await InsertSalesLineAsync(dbContext, salesSlipNumber, customerCode, "__TSTBIL_RCC02", 10000m);

            await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 20), slipRemarks: null, lines: [CashLine(5000m)]);
            var receiptSlipNumber2 = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(6000m)]);

            await service.CancelSlipAsync(receiptSlipNumber2);

            var salesLine = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == salesSlipNumber);
            Assert.Equal(SettlementStatus.PartiallySettled, salesLine.SettlementStatus);
            Assert.Equal(5000m, salesLine.SettledAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode, salesSlipNumber);
        }
    }

    // ── 編集ロック（請求締めスナップショット。TODO.md 7-5） ──────────────

    [Fact]
    public async Task 確定済み請求の集計期間内の締め入金は取消できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCL01";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            // 実際の業務順序（入金登録 → その後に請求締めが実行され集計期間に呑み込まれる）を再現するため、
            // 先に入金を登録してから確定済みbillingを挿入する（日付制限の実装後、逆順だと入金登録自体が
            // 拒否されるため。docs/design_document.md 21-4章 申し送り事項R2）。
            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 7, 15), slipRemarks: null, lines: [CashLine(1000m)]);
            await InsertBillingAsync(
                dbContext, "__TSTBIL_RCL01", customerCode, 10000m, billingDate: new DateOnly(2026, 7, 20));

            var ex = await Assert.ThrowsAsync<ReceiptEntryException>(() => service.CancelSlipAsync(receiptSlipNumber));
            Assert.Contains("請求締め", ex.Message);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 確定済み請求の集計期間内の締め入金は訂正できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCL02";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            // 実際の業務順序（入金登録 → その後に請求締めが実行され集計期間に呑み込まれる）を再現するため、
            // 先に入金を登録してから確定済みbillingを挿入する（日付制限の実装後、逆順だと入金登録自体が
            // 拒否されるため。docs/design_document.md 21-4章 申し送り事項R2）。
            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 7, 15), slipRemarks: null, lines: [CashLine(1000m)]);
            await InsertBillingAsync(
                dbContext, "__TSTBIL_RCL02", customerCode, 10000m, billingDate: new DateOnly(2026, 7, 20));
            var loadedLineNumbers = await LoadLineNumbersAsync(dbContext, receiptSlipNumber);

            var ex = await Assert.ThrowsAsync<ReceiptEntryException>(() => service.UpdateAsync(
                receiptSlipNumber, new DateOnly(2026, 7, 15), null,
                [new ReceiptLineCorrection(loadedLineNumbers[0], CashMethod, null, null, 2000m, null)],
                loadedLineNumbers));
            Assert.Contains("請求締め", ex.Message);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 締め解除すると請求締め済みだった締め入金が取消できるようになる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var releaseService = scope.ServiceProvider.GetRequiredService<BillingReleaseService>();
        var customerCode = "__TSTRCL03";
        const string billingNumber = "__TSTBIL_RCL03";
        // 締め解除はbilling_date単位で走査するため、seedデータのBIL_INV001（billing_date=2026-07-20）
        // と衝突しない専用の日付を使う（2026-09-15、ReleaseAsync→ReleaseByBillingDateAsyncへの変更に伴う対応）。
        var billingDate = new DateOnly(2026, 7, 22);
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            // 実際の業務順序（入金登録 → その後に請求締めが実行され集計期間に呑み込まれる）を再現するため、
            // 先に入金を登録してから確定済みbillingを挿入する（日付制限の実装後、逆順だと入金登録自体が
            // 拒否されるため。docs/design_document.md 21-4章 申し送り事項R2）。
            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 7, 15), slipRemarks: null, lines: [CashLine(1000m)]);
            await InsertBillingAsync(dbContext, billingNumber, customerCode, 10000m, billingDate: billingDate);

            await Assert.ThrowsAsync<ReceiptEntryException>(() => service.CancelSlipAsync(receiptSlipNumber));

            await releaseService.ReleaseByBillingDateAsync(billingDate);
            dbContext.ChangeTracker.Clear();

            await service.CancelSlipAsync(receiptSlipNumber);

            var receiptLines = await ReloadReceiptAsync(dbContext, receiptSlipNumber);
            Assert.All(receiptLines, l => Assert.True(l.IsDeleted));
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 訂正で入金日付を確定済み請求の期間内へ移動する変更は拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCL04";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(
                dbContext, "__TSTBIL_RCL04", customerCode, 10000m, billingDate: new DateOnly(2026, 7, 20));

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(1000m)]);
            var loadedLineNumbers = await LoadLineNumbersAsync(dbContext, receiptSlipNumber);

            var ex = await Assert.ThrowsAsync<ReceiptEntryException>(() => service.UpdateAsync(
                receiptSlipNumber, new DateOnly(2026, 7, 10), null,
                [new ReceiptLineCorrection(loadedLineNumbers[0], CashMethod, null, null, 1000m, null)],
                loadedLineNumbers));
            Assert.Contains("訂正後の内容は編集ロック対象になる", ex.Message);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 月次締め確定済みの年月の締め入金は取消できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCL05";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(1000m)]);

            await InsertMonthlyClosingAsync(dbContext, customerCode, new DateOnly(2026, 8, 31));

            var ex = await Assert.ThrowsAsync<ReceiptEntryException>(() => service.CancelSlipAsync(receiptSlipNumber));
            Assert.Contains("月次締め", ex.Message);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    // ── 日付制限（申し送り事項R2の解消。docs/design_document.md 21-4章） ──────────

    [Fact]
    public async Task 請求締め済み期間の日付では入金を新規登録できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRDL01";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(
                dbContext, "__TSTBIL_RDL01", customerCode, 10000m, billingDate: new DateOnly(2026, 9, 30));

            var ex = await Assert.ThrowsAsync<ReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 9, 30), slipRemarks: null, lines: [CashLine(1000m)]));
            Assert.Contains("請求締め済み", ex.Message);

            var exBefore = await Assert.ThrowsAsync<ReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 9, 29), slipRemarks: null, lines: [CashLine(1000m)]));
            Assert.Contains("請求締め済み", exBefore.Message);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 請求締切日の翌日の入金は登録できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRDL02";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(
                dbContext, "__TSTBIL_RDL02", customerCode, 10000m, billingDate: new DateOnly(2026, 9, 30));

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 10, 1), slipRemarks: null, lines: [CashLine(1000m)]);

            Assert.Equal(8, receiptSlipNumber.Length);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 確定済み請求が無い得意先は過去日付でも入金を登録できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRDL03";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2020, 1, 1), slipRemarks: null, lines: [CashLine(1000m)]);

            Assert.Equal(8, receiptSlipNumber.Length);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 締め解除された請求の期間は入金登録の日付制限を受けない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var releaseService = scope.ServiceProvider.GetRequiredService<BillingReleaseService>();
        var customerCode = "__TSTRDL04";
        var billingDate = new DateOnly(2026, 9, 30);
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(dbContext, "__TSTBIL_RDL04", customerCode, 10000m, billingDate: billingDate);

            await Assert.ThrowsAsync<ReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, billingDate, slipRemarks: null, lines: [CashLine(1000m)]));

            await releaseService.ReleaseByBillingDateAsync(billingDate);
            dbContext.ChangeTracker.Clear();

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, billingDate, slipRemarks: null, lines: [CashLine(1000m)]);

            Assert.Equal(8, receiptSlipNumber.Length);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    // ── 訂正の充当再構築（TODO.md 7-5） ──────────────────────────────

    [Fact]
    public async Task 締め入金の金額を訂正すると自分自身の旧充当を除いた残高に対して再配分される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCU01";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(dbContext, "__TSTBIL_RCU01", customerCode, 10000m);

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(10000m)]);
            var loadedLineNumbers = await LoadLineNumbersAsync(dbContext, receiptSlipNumber);

            // 全額充当済み（outstanding=0）の状態から8,000円へ減額訂正する。SaveChangesAsyncを
            // 挟まずに再配分すると自分自身の旧充当（10,000円）が見えたままになり outstanding が
            // 0のまま判定され、8,000円全額が前受行に落ちる（Q1の回帰）。
            await service.UpdateAsync(
                receiptSlipNumber, new DateOnly(2026, 8, 25), null,
                [new ReceiptLineCorrection(loadedLineNumbers[0], CashMethod, null, null, 8000m, null)],
                loadedLineNumbers);

            var allocations = (await ReloadAllocationAsync(dbContext, receiptSlipNumber))
                .Where(a => !a.IsDeleted).ToList();
            var billingAllocation = Assert.Single(allocations);
            Assert.Equal("__TSTBIL_RCU01", billingAllocation.BillingNumber);
            Assert.Equal(8000m, billingAllocation.AllocatedAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 締め入金の訂正で行を削除すると充当も再構築される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCU02";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            await InsertBillingAsync(dbContext, "__TSTBIL_RCU02", customerCode, 10000m);

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLine(4000m), BankTransferLine(6000m)]);
            var loadedLines = await dbContext.Receipts
                .Where(r => r.ReceiptSlipNumber == receiptSlipNumber && !r.IsDeleted)
                .OrderBy(r => r.LineNumber)
                .ToListAsync();
            var cashLineNumber = loadedLines.Single(l => l.DepositMethodCode == CashMethod).LineNumber;
            var loadedLineNumbers = loadedLines.Select(l => l.LineNumber).ToList();

            // 銀行振込の行を削除し、現金4,000円のみ残す。
            await service.UpdateAsync(
                receiptSlipNumber, new DateOnly(2026, 8, 25), null,
                [new ReceiptLineCorrection(cashLineNumber, CashMethod, null, null, 4000m, null)],
                loadedLineNumbers);

            var allocations = (await ReloadAllocationAsync(dbContext, receiptSlipNumber))
                .Where(a => !a.IsDeleted).ToList();
            var billingAllocation = Assert.Single(allocations);
            Assert.Equal(4000m, billingAllocation.AllocatedAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, customerCode);
        }
    }

    [Fact]
    public async Task 他ユーザーが明細行を追加していた締め入金は訂正できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        var customerCode = "__TSTRCU03";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            var receiptSlipNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLine(1000m)]);
            var loadedLineNumbers = await LoadLineNumbersAsync(dbContext, receiptSlipNumber);

            // 別ユーザーが行を追加した状況を再現する。
            var now = DateTime.Now;
            dbContext.Receipts.Add(new ReceiptEntity
            {
                ReceiptSlipNumber = receiptSlipNumber,
                LineNumber = 99,
                ReceiptDate = new DateOnly(2026, 8, 25),
                CustomerCode = customerCode,
                TaxUnit = TaxUnit.Invoice,
                CustomerName = "テスト用得意先",
                DepositMethodCode = CashMethod,
                Amount = 500m,
                AllocationStatus = AllocationStatus.Unallocated,
                CreatedBy = "TEST",
                CreatedAt = now,
                UpdatedBy = "TEST",
                UpdatedAt = now,
            });
            await dbContext.SaveChangesAsync();

            await Assert.ThrowsAsync<SlipConcurrencyException>(() => service.UpdateAsync(
                receiptSlipNumber, new DateOnly(2026, 8, 25), null,
                [new ReceiptLineCorrection(loadedLineNumbers[0], CashMethod, null, null, 2000m, null)],
                loadedLineNumbers));
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

    /// <summary>訂正（<see cref="ReceiptEntryService.UpdateAsync"/>）の<c>loadedLineNumbers</c>引数を取得する。</summary>
    private static Task<List<short>> LoadLineNumbersAsync(BmcsDbContext dbContext, string receiptSlipNumber) =>
        dbContext.Receipts
            .Where(r => r.ReceiptSlipNumber == receiptSlipNumber && !r.IsDeleted)
            .Select(r => r.LineNumber)
            .ToListAsync();

    private static Task InsertMonthlyClosingAsync(BmcsDbContext dbContext, string customerCode, DateOnly closingDate)
    {
        var now = DateTime.Now;
        dbContext.MonthlyClosings.Add(new MonthlyClosingEntity
        {
            ClosingDate = closingDate,
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Invoice,
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
            ClosingStatus = ClosingStatus.Confirmed,
            ConfirmedAt = now,
            ConfirmedBy = "TEST",
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 作成したテストデータを後始末する。<see cref="ReceiptEntryService.SaveNewAsync"/>は自前で
    /// トランザクションをコミットするため、<c>SettlementServiceTests</c>のような外側Rollback方式は
    /// 使えず、明示的な物理削除が必要（<c>DetailInvoiceServiceTests</c>と同じ方式）。
    /// </summary>
    private static async Task CleanupAsync(
        BmcsDbContext dbContext, string customerCode, string? salesSlipNumber = null)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.monthly_closing WHERE customer_code = {customerCode}");
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
