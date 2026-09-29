using bmcs_app.Application.Billing;
using bmcs_app.Application.Common;
using bmcs_app.Application.Receipt;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Receipt;

/// <summary>
/// 明細入金画面（TODO.md 7-4）・明細入金の取消訂正（TODO.md 7-5）の結合テスト。開発用ライブDB
/// （172.16.3.171）に対して実行する（docs/architecture.md 16章）。都度得意先（内税明細単位）専用。
/// 充当先は売上明細行の直接指定（target_type=1）と明細請求書まるごと1行（target_type=2）の2種類
/// （docs/design_document.md 18章）。
/// </summary>
/// <remarks>
/// <see cref="DetailReceiptEntryService.SaveNewAsync"/>／<see cref="DetailReceiptEntryService.UpdateAsync"/>／
/// <see cref="DetailReceiptEntryService.CancelSlipAsync"/> はいずれも自前で<c>BeginTransactionAsync</c>する
/// ため、<c>SettlementServiceTests</c>のような「外側をトランザクションで包む」方式は使えない。
/// <see cref="ReceiptEntryServiceTests"/>と同じ明示クリーンアップ方式を採る。
/// </remarks>
public class DetailReceiptEntryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string BankAccountCode = "BNK001";
    private const string CashMethod = "CASH";
    private const string BankTransferMethod = "TRANSFER";
    private const string PromissoryNoteMethod = "NOTE";

    [Fact]
    public async Task 売上明細行を直接指定すると全額充当され消込完了になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC01";
        var slip = "__TSTDRC_SAL01";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1100m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales(slip, 1)]);

            var lines = await ReloadAsync(dbContext, detailReceiptNumber);
            Assert.Single(lines);
            Assert.Equal(1100m, lines[0].AllocatedAmount);
            Assert.Equal(1100m, lines[0].ReceiptAmount);
            Assert.Equal(AllocationStatus.FullyAllocated, lines[0].AllocationStatus);

            var salesLine = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slip);
            Assert.Equal(SettlementStatus.FullySettled, salesLine.SettlementStatus);
            Assert.Equal(1100m, salesLine.SettledAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 明細請求書をまるごと指定すると連携先の全売上明細行が消込完了になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, detailInvoiceService) = Resolve(scope);
        var customerCode = "__TSTDRC02";
        var slip1 = "__TSTDRC_SAL02A";
        var slip2 = "__TSTDRC_SAL02B";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip1, 1, new DateOnly(2026, 8, 1), 1m, 1100m, TaxCategory.Standard, 10m, RoundingType.Floor));
            dbContext.Sales.Add(NewSalesLine(customerCode, slip2, 1, new DateOnly(2026, 8, 1), 1m, 2200m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var issued = await detailInvoiceService.IssueAsync(
                customerCode, "宛名", new DateOnly(2026, 8, 5), [(slip1, (short)1), (slip2, (short)1)]);

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToInvoice(issued.DetailInvoiceNumber)]);

            var lines = await ReloadAsync(dbContext, detailReceiptNumber);
            Assert.Single(lines);
            Assert.Equal(3300m, lines[0].AllocatedAmount);
            Assert.Equal(AllocationStatus.FullyAllocated, lines[0].AllocationStatus);

            var salesLines = await dbContext.Sales.AsNoTracking()
                .Where(s => s.SalesSlipNumber == slip1 || s.SalesSlipNumber == slip2)
                .ToListAsync();
            Assert.All(salesLines, s => Assert.Equal(SettlementStatus.FullySettled, s.SettlementStatus));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip1, slip2]);
        }
    }

    [Fact]
    public async Task 直接指定と明細請求書経由が混在する伝票でも正しく消込される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, detailInvoiceService) = Resolve(scope);
        var customerCode = "__TSTDRC03";
        var directSlip = "__TSTDRC_SAL03D";
        var invoicedSlip = "__TSTDRC_SAL03I";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, directSlip, 1, new DateOnly(2026, 8, 1), 1m, 500m, TaxCategory.Standard, 10m, RoundingType.Floor));
            dbContext.Sales.Add(NewSalesLine(customerCode, invoicedSlip, 1, new DateOnly(2026, 8, 1), 1m, 1500m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var issued = await detailInvoiceService.IssueAsync(
                customerCode, "宛名", new DateOnly(2026, 8, 5), [(invoicedSlip, (short)1)]);

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales(directSlip, 1), CashLineToInvoice(issued.DetailInvoiceNumber)]);

            var lines = await ReloadAsync(dbContext, detailReceiptNumber);
            Assert.Equal(2, lines.Count);
            Assert.Equal(2000m, lines[0].ReceiptAmount);

            var salesLines = await dbContext.Sales.AsNoTracking()
                .Where(s => s.SalesSlipNumber == directSlip || s.SalesSlipNumber == invoicedSlip)
                .ToListAsync();
            Assert.All(salesLines, s => Assert.Equal(SettlementStatus.FullySettled, s.SettlementStatus));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [directSlip, invoicedSlip]);
        }
    }

    [Fact]
    public async Task 一部消込済みの売上明細行に残額を直接指定すると過充当にならず消込完了になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC04";
        var slip = "__TSTDRC_SAL04";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor,
                settlementStatus: SettlementStatus.PartiallySettled, settledAmount: 400m));
            await dbContext.SaveChangesAsync();

            // 400円分の「先行入金」は、settled_amount のキャッシュ値と整合させるため裏付けとなる
            // detail_receipt を実際に挿入する（SettlementService は毎回実データから全件
            // 再計算するため、裏付けの無いキャッシュ値だけでは新規入金の登録時に上書きされてしまう）。
            await InsertPriorDetailReceiptAsync(dbContext, "__TSTDRC_PRIOR04", customerCode, slip, 1, 400m);

            var candidates = await service.GetSalesLineCandidatesAsync(customerCode);
            var candidate = Assert.Single(candidates, c => c.SalesSlipNumber == slip);
            Assert.Equal(600m, candidate.RemainingAmount);

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales(slip, 1)]);

            var lines = await ReloadAsync(dbContext, detailReceiptNumber);
            Assert.Equal(600m, lines[0].AllocatedAmount);

            var salesLine = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slip);
            Assert.Equal(SettlementStatus.FullySettled, salesLine.SettlementStatus);
            Assert.Equal(1000m, salesLine.SettledAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 明細単位で意図した売上だけが消込され対象外の売上は未消込のまま残る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC05";
        var targetSlip = "__TSTDRC_SAL05A";
        var otherSlip = "__TSTDRC_SAL05B";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, targetSlip, 1, new DateOnly(2026, 8, 1), 1m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor));
            dbContext.Sales.Add(NewSalesLine(customerCode, otherSlip, 1, new DateOnly(2026, 8, 1), 1m, 2000m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales(targetSlip, 1)]);

            var target = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == targetSlip);
            var other = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == otherSlip);
            Assert.Equal(SettlementStatus.FullySettled, target.SettlementStatus);
            Assert.Equal(SettlementStatus.Unsettled, other.SettlementStatus);
            Assert.Equal(0m, other.SettledAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [targetSlip, otherSlip]);
        }
    }

    [Fact]
    public async Task 返品行を正の行と同時に直接指定すると差引で全額充当される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC06";
        var saleSlip = "__TSTDRC_SAL06A";
        var returnSlip = "__TSTDRC_SAL06B";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, saleSlip, 1, new DateOnly(2026, 8, 1), 1m, 1100m, TaxCategory.Standard, 10m, RoundingType.Floor));
            dbContext.Sales.Add(NewSalesLine(customerCode, returnSlip, 1, new DateOnly(2026, 8, 2), -1m, 100m, TaxCategory.Standard, 10m, RoundingType.Floor,
                slipType: SlipType.Return));
            await dbContext.SaveChangesAsync();

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales(saleSlip, 1), CashLineToSales(returnSlip, 1)]);

            var lines = await ReloadAsync(dbContext, detailReceiptNumber);
            Assert.Equal(1000m, lines[0].ReceiptAmount);

            var saleLine = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == saleSlip);
            var returnLine = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == returnSlip);
            Assert.Equal(SettlementStatus.FullySettled, saleLine.SettlementStatus);
            Assert.Equal(SettlementStatus.FullySettled, returnLine.SettlementStatus);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [saleSlip, returnSlip]);
        }
    }

    [Fact]
    public async Task 返品行単独の指定は入金合計が0以下になるため例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC07";
        var returnSlip = "__TSTDRC_SAL07";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, returnSlip, 1, new DateOnly(2026, 8, 1), -1m, 100m, TaxCategory.Standard, 10m, RoundingType.Floor,
                slipType: SlipType.Return));
            await dbContext.SaveChangesAsync();

            await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales(returnSlip, 1)]));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [returnSlip]);
        }
    }

    [Fact]
    public async Task 明細0件の場合は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC08";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);

            await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: []));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], []);
        }
    }

    [Fact]
    public async Task 同じ売上明細行の重複指定は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC09";
        var slip = "__TSTDRC_SAL09";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales(slip, 1), CashLineToSales(slip, 1)]));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 請求書とその連携売上明細行を同時に指定すると例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, detailInvoiceService) = Resolve(scope);
        var customerCode = "__TSTDRC10";
        var slip = "__TSTDRC_SAL10";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1100m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var issued = await detailInvoiceService.IssueAsync(
                customerCode, "宛名", new DateOnly(2026, 8, 5), [(slip, (short)1)]);

            await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales(slip, 1), CashLineToInvoice(issued.DetailInvoiceNumber)]));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 締め得意先はこの画面で入金登録できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC11";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode, taxUnit: TaxUnit.Invoice);

            await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales("__NOEXIST", 1)]));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], []);
        }
    }

    [Fact]
    public async Task 存在しない得意先は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (_, service, _) = Resolve(scope);

        await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.SaveNewAsync(
            "__TSTDRC_NOEXIST", new DateOnly(2026, 8, 25), slipRemarks: null,
            lines: [CashLineToSales("__NOEXIST", 1)]));
    }

    [Fact]
    public async Task 無効化済み得意先は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC12";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode, isDeleted: true);

            await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales("__NOEXIST", 1)]));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], []);
        }
    }

    [Fact]
    public async Task 振込で入金先口座が未指定の場合は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC13";
        var slip = "__TSTDRC_SAL13";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [new DetailReceiptLineInput(
                    DetailReceiptTargetType.SalesLine, slip, 1, null, BankTransferMethod, null, null)]));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 振込は口座が保存され現金の行は指定した口座が無視されてnullになる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC19";
        var slip1 = "__TSTDRC_SAL19A";
        var slip2 = "__TSTDRC_SAL19B";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip1, 1, new DateOnly(2026, 8, 1), 1m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor));
            dbContext.Sales.Add(NewSalesLine(customerCode, slip2, 1, new DateOnly(2026, 8, 1), 1m, 500m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines:
                [
                    new DetailReceiptLineInput(
                        DetailReceiptTargetType.SalesLine, slip1, 1, null, BankTransferMethod, BankAccountCode, null),
                    // 現金の行に口座コードを渡しても保存時に無視される（修正点7）。
                    new DetailReceiptLineInput(
                        DetailReceiptTargetType.SalesLine, slip2, 1, null, CashMethod, BankAccountCode, null),
                ]);

            var lines = await ReloadAsync(dbContext, detailReceiptNumber);
            var transferLine = lines.Single(l => l.DepositMethodCode == BankTransferMethod);
            var cashLine = lines.Single(l => l.DepositMethodCode == CashMethod);
            Assert.Equal(BankAccountCode, transferLine.BankAccountCode);
            Assert.Null(cashLine.BankAccountCode);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip1, slip2]);
        }
    }

    [Fact]
    public async Task 入金方法に手形を指定すると例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC14";
        var slip = "__TSTDRC_SAL14";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [new DetailReceiptLineInput(
                    DetailReceiptTargetType.SalesLine, slip, 1, null, PromissoryNoteMethod, null, null)]));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 消込完了済みの売上明細行を直接指定すると候補条件違反で例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC15";
        var slip = "__TSTDRC_SAL15";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor,
                settlementStatus: SettlementStatus.FullySettled, settledAmount: 1000m));
            await dbContext.SaveChangesAsync();

            await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales(slip, 1)]));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 金額ゼロの売上明細行は候補から除外される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC16";
        var slip = "__TSTDRC_SAL16";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            var zeroLine = NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 0m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor);
            zeroLine.Amount = 0m;
            zeroLine.TaxAmount = 0m;
            dbContext.Sales.Add(zeroLine);
            await dbContext.SaveChangesAsync();

            var candidates = await service.GetSalesLineCandidatesAsync(customerCode);
            Assert.DoesNotContain(candidates, c => c.SalesSlipNumber == slip);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 明細請求書の金額と連携先売上合計が一致しない場合は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, detailInvoiceService) = Resolve(scope);
        var customerCode = "__TSTDRC17";
        var slip = "__TSTDRC_SAL17";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1100m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var issued = await detailInvoiceService.IssueAsync(
                customerCode, "宛名", new DateOnly(2026, 8, 5), [(slip, (short)1)]);

            // データ補正等によるズレを人為的に再現する（本来発生しないはずの不整合）。
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.sales SET amount = 1 WHERE sales_slip_number = {slip}");

            await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToInvoice(issued.DetailInvoiceNumber)]));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 保存した伝票を明細入金Noで読み込める()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDRC18";
        var slip = "__TSTDRC_SAL18";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: "テスト摘要",
                lines: [CashLineToSales(slip, 1)]);

            var lines = await service.GetByNumberAsync(detailReceiptNumber);
            Assert.Single(lines);
            Assert.Equal(customerCode, lines[0].CustomerCode);
            Assert.Equal("テスト摘要", lines[0].SlipRemarks);
            Assert.Equal(CashMethod, lines[0].DepositMethodCode);
            Assert.Equal(1000m, lines[0].AllocatedAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    // ── 取消（TODO.md 7-5） ──────────────────────────────────────

    [Fact]
    public async Task 明細入金_直接指定_を取消すると対象売上明細行が未消込に戻る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDCC01";
        var slip = "__TSTDRC_SCC01";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1100m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLineToSales(slip, 1)]);

            await service.CancelSlipAsync(detailReceiptNumber);

            var salesLine = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slip);
            Assert.Equal(SettlementStatus.Unsettled, salesLine.SettlementStatus);
            Assert.Equal(0m, salesLine.SettledAmount);

            var lines = await ReloadAsync(dbContext, detailReceiptNumber);
            Assert.All(lines, l => Assert.True(l.IsDeleted));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 明細入金_明細請求書指定_を取消すると連携先売上が未消込に戻り明細請求書を再取消できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, detailInvoiceService) = Resolve(scope);
        var customerCode = "__TSTDCC02";
        var slip = "__TSTDRC_SCC02";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1100m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var issued = await detailInvoiceService.IssueAsync(
                customerCode, "宛名", new DateOnly(2026, 8, 5), [(slip, (short)1)]);

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToInvoice(issued.DetailInvoiceNumber)]);

            // 消込済みのため取消できない（DetailInvoiceService.CancelAsyncの既存ガード）。
            await Assert.ThrowsAsync<DetailInvoiceException>(
                () => detailInvoiceService.CancelAsync(issued.DetailInvoiceNumber));

            await service.CancelSlipAsync(detailReceiptNumber);

            var salesLine = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slip);
            Assert.Equal(SettlementStatus.Unsettled, salesLine.SettlementStatus);

            // 消込が巻き戻ったため、明細請求書の取消ガードも解除される（完了条件の直接検証）。
            await detailInvoiceService.CancelAsync(issued.DetailInvoiceNumber);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 返品行を含む明細入金を取消すると返品行が未消込に戻る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDCC03";
        var saleSlip = "__TSTDRC_SCC03A";
        var returnSlip = "__TSTDRC_SCC03B";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, saleSlip, 1, new DateOnly(2026, 8, 1), 1m, 1100m, TaxCategory.Standard, 10m, RoundingType.Floor));
            dbContext.Sales.Add(NewSalesLine(customerCode, returnSlip, 1, new DateOnly(2026, 8, 2), -1m, 100m, TaxCategory.Standard, 10m, RoundingType.Floor,
                slipType: SlipType.Return));
            await dbContext.SaveChangesAsync();

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales(saleSlip, 1), CashLineToSales(returnSlip, 1)]);

            await service.CancelSlipAsync(detailReceiptNumber);

            var returnLine = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == returnSlip);
            Assert.Equal(SettlementStatus.Unsettled, returnLine.SettlementStatus);
            Assert.Equal(0m, returnLine.SettledAmount);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [saleSlip, returnSlip]);
        }
    }

    [Fact]
    public async Task 月次締め確定済みの年月の明細入金は取消できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDCL01";
        var slip = "__TSTDRC_SCL01";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1100m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLineToSales(slip, 1)]);

            await InsertMonthlyClosingAsync(dbContext, customerCode, new DateOnly(2026, 8, 31));

            var ex = await Assert.ThrowsAsync<DetailReceiptEntryException>(
                () => service.CancelSlipAsync(detailReceiptNumber));
            Assert.Contains("月次締め", ex.Message);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.monthly_closings WHERE customer_code = {customerCode}");
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    // ── 訂正（TODO.md 7-5） ──────────────────────────────────────

    [Fact]
    public async Task 明細入金の訂正では充当先の候補条件を再評価しないため他の伝票の影響を受けない()
    {
        // 明細請求書指定の明細入金は、保存した時点で自分自身が「この請求書を指す未削除の明細入金」
        // として存在するため、候補条件（BuildDetailInvoiceCandidateQuery）を訂正時に再実行すると
        // 常に自分自身の存在で弾かれてしまう（P3の回帰）。UpdateAsyncは候補条件を再評価せず、
        // 読込時の充当額をそのまま保持することでこれを構造的に回避している。
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, detailInvoiceService) = Resolve(scope);
        var customerCode = "__TSTDCU01";
        var slip = "__TSTDRC_SCU01";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1100m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var issued = await detailInvoiceService.IssueAsync(
                customerCode, "宛名", new DateOnly(2026, 8, 5), [(slip, (short)1)]);

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToInvoice(issued.DetailInvoiceNumber)]);
            var loaded = await ReloadAsync(dbContext, detailReceiptNumber);
            var loadedLineNumbers = loaded.Select(l => l.LineNumber).ToList();

            await service.UpdateAsync(
                detailReceiptNumber, new DateOnly(2026, 8, 26), "訂正後の摘要",
                [new DetailReceiptLineCorrection(loadedLineNumbers[0], CashMethod, null, null)],
                loadedLineNumbers);

            var corrected = await ReloadAsync(dbContext, detailReceiptNumber);
            var activeLine = Assert.Single(corrected, l => !l.IsDeleted);
            Assert.Equal(1100m, activeLine.AllocatedAmount);
            Assert.Equal(1100m, activeLine.ReceiptAmount);
            Assert.Equal(new DateOnly(2026, 8, 26), activeLine.ReceiptDate);
            Assert.Equal("訂正後の摘要", activeLine.SlipRemarks);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 明細入金の訂正で行を削除すると残る全行の入金額が新しい合計に揃って更新される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDCU02";
        var slipA = "__TSTDRC_SCU02A";
        var slipB = "__TSTDRC_SCU02B";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slipA, 1, new DateOnly(2026, 8, 1), 1m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor));
            dbContext.Sales.Add(NewSalesLine(customerCode, slipB, 1, new DateOnly(2026, 8, 1), 1m, 500m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null,
                lines: [CashLineToSales(slipA, 1), CashLineToSales(slipB, 1)]);
            var loaded = await ReloadAsync(dbContext, detailReceiptNumber);
            var keepLineNumber = loaded.Single(l => l.TargetSalesSlipNumber == slipA).LineNumber;
            var loadedLineNumbers = loaded.Select(l => l.LineNumber).ToList();

            // slipB を指す行を削除し、slipA を指す行だけを残す。
            await service.UpdateAsync(
                detailReceiptNumber, new DateOnly(2026, 8, 25), null,
                [new DetailReceiptLineCorrection(keepLineNumber, CashMethod, null, null)],
                loadedLineNumbers);

            var corrected = (await ReloadAsync(dbContext, detailReceiptNumber)).Where(l => !l.IsDeleted).ToList();
            var remaining = Assert.Single(corrected);
            Assert.Equal(1000m, remaining.AllocatedAmount);
            Assert.Equal(1000m, remaining.ReceiptAmount);

            var slipBLine = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slipB);
            Assert.Equal(SettlementStatus.Unsettled, slipBLine.SettlementStatus);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slipA, slipB]);
        }
    }

    [Fact]
    public async Task 明細入金の訂正で新しい充当先を追加しようとすると拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDCU03";
        var slip = "__TSTDRC_SCU03";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLineToSales(slip, 1)]);
            var loaded = await ReloadAsync(dbContext, detailReceiptNumber);
            var loadedLineNumbers = loaded.Select(l => l.LineNumber).ToList();
            var newLineNumber = (short)(loadedLineNumbers.Max() + 1);

            var ex = await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.UpdateAsync(
                detailReceiptNumber, new DateOnly(2026, 8, 25), null,
                [
                    new DetailReceiptLineCorrection(loadedLineNumbers[0], CashMethod, null, null),
                    new DetailReceiptLineCorrection(newLineNumber, CashMethod, null, null),
                ],
                loadedLineNumbers));
            Assert.Contains("充当先を追加する", ex.Message);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 明細入金の訂正で全行を削除しようとすると拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDCU04";
        var slip = "__TSTDRC_SCU04";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLineToSales(slip, 1)]);
            var loadedLineNumbers = (await ReloadAsync(dbContext, detailReceiptNumber)).Select(l => l.LineNumber).ToList();

            var ex = await Assert.ThrowsAsync<DetailReceiptEntryException>(() => service.UpdateAsync(
                detailReceiptNumber, new DateOnly(2026, 8, 25), null, [], loadedLineNumbers));
            Assert.Contains("取消をご利用ください", ex.Message);
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    [Fact]
    public async Task 他ユーザーが明細行を追加していた明細入金は訂正できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service, _) = Resolve(scope);
        var customerCode = "__TSTDCU05";
        var slip = "__TSTDRC_SCU05";
        try
        {
            await InsertCustomerAsync(dbContext, customerCode);
            dbContext.Sales.Add(NewSalesLine(customerCode, slip, 1, new DateOnly(2026, 8, 1), 1m, 1000m, TaxCategory.Standard, 10m, RoundingType.Floor));
            await dbContext.SaveChangesAsync();

            var detailReceiptNumber = await service.SaveNewAsync(
                customerCode, new DateOnly(2026, 8, 25), slipRemarks: null, lines: [CashLineToSales(slip, 1)]);
            var loadedLineNumbers = (await ReloadAsync(dbContext, detailReceiptNumber)).Select(l => l.LineNumber).ToList();

            // 別ユーザーが行を追加した状況を再現する（充当先はダミーで別の売上行を指す必要はなく、
            // 行の集合が変化したことだけを検出できればよい）。
            var now = DateTime.Now;
            dbContext.DetailReceipts.Add(new DetailReceipt
            {
                DetailReceiptNumber = detailReceiptNumber,
                LineNumber = 99,
                ReceiptDate = new DateOnly(2026, 8, 25),
                CustomerCode = customerCode,
                CustomerName = "テスト用都度得意先",
                DepositMethodCode = CashMethod,
                ReceiptAmount = 1000m,
                TargetType = DetailReceiptTargetType.SalesLine,
                TargetSalesSlipNumber = slip,
                TargetSalesLineNumber = 1,
                AllocatedAmount = 0m,
                FeeAdjustmentAmount = 0m,
                AllocationStatus = AllocationStatus.Unallocated,
                CreatedBy = "TEST",
                CreatedAt = now,
                UpdatedBy = "TEST",
                UpdatedAt = now,
            });
            await dbContext.SaveChangesAsync();

            await Assert.ThrowsAsync<SlipConcurrencyException>(() => service.UpdateAsync(
                detailReceiptNumber, new DateOnly(2026, 8, 25), null,
                [new DetailReceiptLineCorrection(loadedLineNumbers[0], CashMethod, null, null)],
                loadedLineNumbers));
        }
        finally
        {
            await CleanupAsync(dbContext, [customerCode], [slip]);
        }
    }

    private static DetailReceiptLineInput CashLineToSales(string salesSlipNumber, short lineNumber) =>
        new(DetailReceiptTargetType.SalesLine, salesSlipNumber, lineNumber, null, CashMethod, null, null);

    private static DetailReceiptLineInput CashLineToInvoice(string detailInvoiceNumber) =>
        new(DetailReceiptTargetType.DetailInvoice, null, null, detailInvoiceNumber, CashMethod, null, null);

    private static (BmcsDbContext DbContext, DetailReceiptEntryService Service, DetailInvoiceService DetailInvoiceService) Resolve(
        AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<DetailReceiptEntryService>(),
        scope.ServiceProvider.GetRequiredService<DetailInvoiceService>());

    private static async Task InsertCustomerAsync(
        BmcsDbContext dbContext, string customerCode, TaxUnit taxUnit = TaxUnit.Line, bool isDeleted = false)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用都度得意先",
            SalesEmployeeCode = "EMP001",
            ClosingDay = taxUnit == TaxUnit.Line ? (byte)0 : (byte)15,
            TaxUnit = taxUnit,
            RoundingType = RoundingType.Floor,
            BillingCustomerCode = customerCode,
            PrintRepresentativeFlag = false,
            IsDeleted = isDeleted,
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
        DateOnly slipDate,
        decimal quantity,
        decimal unitPrice,
        TaxCategory taxCategory,
        decimal taxRate,
        RoundingType roundingType,
        SlipType slipType = SlipType.Sales,
        SettlementStatus settlementStatus = SettlementStatus.Unsettled,
        decimal settledAmount = 0m,
        bool isDeleted = false)
    {
        var amount = ConsumptionTaxCalculator.CalculateLineAmount(quantity, unitPrice, roundingType);
        var taxAmount = ConsumptionTaxCalculator.CalculateInternalTaxAmount(new TaxLine(taxCategory, taxRate, amount), roundingType);
        var now = DateTime.Now;

        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = lineNumber,
            SlipDate = slipDate,
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Line,
            CustomerName = "テスト用都度得意先",
            SlipType = slipType,
            ProductCode = "PRD001",
            ProductName = "テスト用商品",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = amount,
            CostPrice = 0m,
            TaxCategory = taxCategory,
            TaxRate = taxRate,
            SlipTaxAmount = null,
            TaxAmount = taxAmount,
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Unbilled,
            SettlementStatus = settlementStatus,
            SettledAmount = settledAmount,
            IsDeleted = isDeleted,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// 一部消込状態を裏付ける「先行入金」レコードを直接挿入する（サービス経由ではなく、既に
    /// 実在したはずの過去の明細入金を模す）。<c>NewSalesLine</c>の<c>settledAmount</c>指定と
    /// 整合させるため、テストのセットアップでのみ使う。
    /// </summary>
    private static async Task InsertPriorDetailReceiptAsync(
        BmcsDbContext dbContext, string detailReceiptNumber, string customerCode,
        string targetSalesSlipNumber, short targetSalesLineNumber, decimal allocatedAmount)
    {
        var now = DateTime.Now;
        dbContext.DetailReceipts.Add(new DetailReceipt
        {
            DetailReceiptNumber = detailReceiptNumber,
            LineNumber = 1,
            ReceiptDate = new DateOnly(2026, 7, 1),
            CustomerCode = customerCode,
            CustomerName = "テスト用都度得意先",
            DepositMethodCode = CashMethod,
            ReceiptAmount = allocatedAmount,
            TargetType = DetailReceiptTargetType.SalesLine,
            TargetSalesSlipNumber = targetSalesSlipNumber,
            TargetSalesLineNumber = targetSalesLineNumber,
            AllocatedAmount = allocatedAmount,
            FeeAdjustmentAmount = 0m,
            AllocationStatus = AllocationStatus.FullyAllocated,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await dbContext.SaveChangesAsync();
    }

    private static Task<List<DetailReceipt>> ReloadAsync(BmcsDbContext dbContext, string detailReceiptNumber) =>
        dbContext.DetailReceipts.AsNoTracking()
            .Where(r => r.DetailReceiptNumber == detailReceiptNumber)
            .OrderBy(r => r.LineNumber)
            .ToListAsync();

    private static Task InsertMonthlyClosingAsync(BmcsDbContext dbContext, string customerCode, DateOnly closingDate)
    {
        var now = DateTime.Now;
        dbContext.MonthlyClosings.Add(new MonthlyClosing
        {
            ClosingDate = closingDate,
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Line,
            CustomerName = "テスト用都度得意先",
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
    /// 作成したテストデータを後始末する。<see cref="DetailReceiptEntryService.SaveNewAsync"/>は
    /// 自前でトランザクションをコミットするため、明示的な物理削除が必要
    /// （<c>ReceiptEntryServiceTests</c>・<c>DetailInvoiceServiceTests</c>と同じ方式）。
    /// FK順: detail_receipt → detail_invoice_sales_line → detail_invoice → sales → customer。
    /// </summary>
    private static async Task CleanupAsync(
        BmcsDbContext dbContext, IReadOnlyList<string> customerCodes, IReadOnlyList<string> salesSlipNumbers)
    {
        foreach (var customerCode in customerCodes)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.detail_receipts WHERE customer_code = {customerCode}");
        }

        foreach (var slipNumber in salesSlipNumbers)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.detail_invoice_sales_lines WHERE sales_slip_number = {slipNumber}");
        }

        foreach (var customerCode in customerCodes)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.detail_invoices WHERE customer_code = {customerCode}");
        }

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
