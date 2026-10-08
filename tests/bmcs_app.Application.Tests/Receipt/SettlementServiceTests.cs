using bmcs_app.Application.Receipt;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BillingEntity = bmcs_app.Domain.Entities.Billing;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using DetailInvoiceEntity = bmcs_app.Domain.Entities.DetailInvoice;
using DetailInvoiceSalesLineEntity = bmcs_app.Domain.Entities.DetailInvoiceSalesLine;
using DetailReceiptEntity = bmcs_app.Domain.Entities.DetailReceipt;
using ReceiptAllocationEntity = bmcs_app.Domain.Entities.ReceiptAllocation;
using ReceiptEntity = bmcs_app.Domain.Entities.Receipt;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Receipt;

/// <summary>
/// 入金消込の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する
/// （docs/architecture.md 16章）。完了条件「登録・取消・訂正のいずれでもキャッシュ列が
/// 実態と一致する」の実証。<c>receipt</c>は支払手段の内訳、充当は<c>receipt_allocation</c>が
/// 別に持つ（docs/design_document.md 17章）。
/// </summary>
/// <remarks>
/// <see cref="SettlementService.RecalculateForBillingGroupAsync"/> は自前で<c>BeginTransactionAsync</c>
/// しないため、<c>OrderStatusServiceTests</c>と同じ「外側をトランザクションで包み、
/// テストの最後に必ずRollbackする」方式が使える。得意先・売上・入金・請求・明細請求書を
/// すべてこのトランザクション内で作成するため、seedデータには一切触れず、後始末の
/// 物理削除も不要。
/// </remarks>
public class SettlementServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerInvoice = "__TSTSTL1"; // 請求単位
    private const string CustomerSlip = "__TSTSTL2"; // 伝票単位
    private const string CustomerLine = "__TSTSTL3"; // 内税明細単位（都度得意先）

    [Fact]
    public async Task 請求単位の得意先で全額入金すると税額分を除いた売上行が消込完了になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice);
            await InsertBillingAsync(dbContext, "__TSTBIL_STL01", CustomerInvoice, TaxUnit.Invoice, currentBillingAmount: 11000m);

            const string slip = "__TSTSAL_STL01";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, 6000m, "__TSTBIL_STL01"),
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 2, 4000m, "__TSTBIL_STL01"));

            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL01", 1, 11000m));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL01", 1, "__TSTBIL_STL01", 11000m));

            var result = await service.RecalculateForBillingGroupAsync(CustomerInvoice);

            var persisted = await ReloadSalesAsync(dbContext, slip);
            Assert.All(persisted, l => Assert.Equal(SettlementStatus.FullySettled, l.SettlementStatus));
            Assert.Equal(6000m, persisted.Single(l => l.LineNumber == 1).SettledAmount);
            Assert.Equal(4000m, persisted.Single(l => l.LineNumber == 2).SettledAmount);
            Assert.Equal(2, result.UpdatedSalesLineCount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 請求単位の得意先で一部入金すると古い順に配分され後続の行は未消込のままになる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice);
            await InsertBillingAsync(dbContext, "__TSTBIL_STL02", CustomerInvoice, TaxUnit.Invoice, currentBillingAmount: 11000m);

            const string slip = "__TSTSAL_STL02";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, 6000m, "__TSTBIL_STL02", slipDate: new DateOnly(2026, 7, 1)),
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 2, 4000m, "__TSTBIL_STL02", slipDate: new DateOnly(2026, 7, 2)));

            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL02", 1, 5500m));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL02", 1, "__TSTBIL_STL02", 5500m));

            await service.RecalculateForBillingGroupAsync(CustomerInvoice);

            var persisted = await ReloadSalesAsync(dbContext, slip);
            var line1 = persisted.Single(l => l.LineNumber == 1);
            var line2 = persisted.Single(l => l.LineNumber == 2);
            Assert.Equal(5500m, line1.SettledAmount);
            Assert.Equal(SettlementStatus.PartiallySettled, line1.SettlementStatus);
            Assert.Equal(0m, line2.SettledAmount);
            Assert.Equal(SettlementStatus.Unsettled, line2.SettlementStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 複数の請求データにまたがる入金は請求ごとの充当額の範囲でそれぞれ配分される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice);
            // billing A(1月・前残0・売上10,000・税1,000)→請求11,000。billing B(2月・前残11,000
            // ・売上5,000・税500)→請求16,500（検証例）。
            await InsertBillingAsync(dbContext, "__TSTBIL_STLA", CustomerInvoice, TaxUnit.Invoice, currentBillingAmount: 11000m, closingYearMonth: "202601");
            await InsertBillingAsync(dbContext, "__TSTBIL_STLB", CustomerInvoice, TaxUnit.Invoice, currentBillingAmount: 16500m, closingYearMonth: "202602");

            const string slipA = "__TSTSAL_STLA";
            const string slipB = "__TSTSAL_STLB";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slipA, 1, 10000m, "__TSTBIL_STLA"),
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slipB, 1, 5000m, "__TSTBIL_STLB"));

            // 入金1件（16,500）を古い順にA(11,000・全額)→B(5,500・一部)へ充当する。
            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STLAB", 1, 16500m));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STLAB", 1, "__TSTBIL_STLA", 11000m),
                NewReceiptAllocationLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STLAB", 2, "__TSTBIL_STLB", 5500m));

            await service.RecalculateForBillingGroupAsync(CustomerInvoice);

            var salesA = await ReloadSalesAsync(dbContext, slipA);
            var salesB = await ReloadSalesAsync(dbContext, slipB);
            Assert.Equal(SettlementStatus.FullySettled, salesA.Single().SettlementStatus);
            Assert.Equal(10000m, salesA.Single().SettledAmount);
            Assert.Equal(SettlementStatus.FullySettled, salesB.Single().SettlementStatus);
            Assert.Equal(5000m, salesB.Single().SettledAmount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 手数料差額は売上行の消込済金額に含まれるが充当ステータスの判定には含まれない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice);
            await InsertBillingAsync(dbContext, "__TSTBIL_STL03", CustomerInvoice, TaxUnit.Invoice, currentBillingAmount: 10000m);

            const string slip = "__TSTSAL_STL03";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, 10000m, "__TSTBIL_STL03"));

            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL03", 1, 9780m));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL03", 1, "__TSTBIL_STL03", 9780m, feeAdjustmentAmount: 220m));

            await service.RecalculateForBillingGroupAsync(CustomerInvoice);

            var persistedSales = await ReloadSalesAsync(dbContext, slip);
            Assert.Equal(SettlementStatus.FullySettled, persistedSales.Single().SettlementStatus);
            Assert.Equal(10000m, persistedSales.Single().SettledAmount);

            var persistedReceipt = await dbContext.Receipts.AsNoTracking()
                .SingleAsync(r => r.ReceiptSlipNumber == "__TSTRCP_STL03");
            // 入金額そのもの(9,780)を全額充当済みのため、手数料差額を含めなくても充当完了。
            Assert.Equal(AllocationStatus.FullyAllocated, persistedReceipt.AllocationStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 返品行を含む請求で全額入金すると売上行と返品行がともに消込完了になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice);
            await InsertBillingAsync(dbContext, "__TSTBIL_STL04", CustomerInvoice, TaxUnit.Invoice, currentBillingAmount: 8000m);

            const string slip = "__TSTSAL_STL04";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, 10000m, "__TSTBIL_STL04"),
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 2, -2000m, "__TSTBIL_STL04", SlipType.Return));

            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL04", 1, 8000m));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL04", 1, "__TSTBIL_STL04", 8000m));

            await service.RecalculateForBillingGroupAsync(CustomerInvoice);

            var persisted = await ReloadSalesAsync(dbContext, slip);
            Assert.All(persisted, l => Assert.Equal(SettlementStatus.FullySettled, l.SettlementStatus));
            Assert.Equal(10000m, persisted.Single(l => l.LineNumber == 1).SettledAmount);
            Assert.Equal(-2000m, persisted.Single(l => l.LineNumber == 2).SettledAmount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 返品行を含む請求で一部入金すると正の売上行だけが一部消込になり返品行は未消込のまま残る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice);
            await InsertBillingAsync(dbContext, "__TSTBIL_STL05", CustomerInvoice, TaxUnit.Invoice, currentBillingAmount: 8000m);

            const string slip = "__TSTSAL_STL05";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, 10000m, "__TSTBIL_STL05", slipDate: new DateOnly(2026, 7, 1)),
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 2, -2000m, "__TSTBIL_STL05", SlipType.Return, slipDate: new DateOnly(2026, 7, 2)));

            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL05", 1, 5000m));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL05", 1, "__TSTBIL_STL05", 5000m));

            await service.RecalculateForBillingGroupAsync(CustomerInvoice);

            var persisted = await ReloadSalesAsync(dbContext, slip);
            var salesLine = persisted.Single(l => l.LineNumber == 1);
            var returnLine = persisted.Single(l => l.LineNumber == 2);
            Assert.Equal(SettlementStatus.PartiallySettled, salesLine.SettlementStatus);
            Assert.Equal(5000m, salesLine.SettledAmount);
            Assert.Equal(SettlementStatus.Unsettled, returnLine.SettlementStatus);
            Assert.Equal(0m, returnLine.SettledAmount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 充当先の無い前受の入金は売上行を消し込まず未充当のままになる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice);
            await InsertBillingAsync(dbContext, "__TSTBIL_STL06", CustomerInvoice, TaxUnit.Invoice, currentBillingAmount: 5000m);

            const string slip = "__TSTSAL_STL06";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, 5000m, "__TSTBIL_STL06"));

            // 充当先の無い前受（receipt_allocationの行を作らない＝充当額合計は常に0）。
            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL06", 1, 3000m));

            await service.RecalculateForBillingGroupAsync(CustomerInvoice);

            var persistedSales = await ReloadSalesAsync(dbContext, slip);
            Assert.Equal(SettlementStatus.Unsettled, persistedSales.Single().SettlementStatus);

            var persistedReceipt = await dbContext.Receipts.AsNoTracking()
                .SingleAsync(r => r.ReceiptSlipNumber == "__TSTRCP_STL06");
            Assert.Equal(AllocationStatus.Unallocated, persistedReceipt.AllocationStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 過入金でも消込済金額は売上金額を超えず超過分は入金側に残る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice);
            await InsertBillingAsync(dbContext, "__TSTBIL_STL07", CustomerInvoice, TaxUnit.Invoice, currentBillingAmount: 10000m);

            const string slip = "__TSTSAL_STL07";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, 10000m, "__TSTBIL_STL07"));

            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL07", 1, 15000m));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL07", 1, "__TSTBIL_STL07", 15000m));

            await service.RecalculateForBillingGroupAsync(CustomerInvoice);

            var persisted = await ReloadSalesAsync(dbContext, slip);
            Assert.Equal(10000m, persisted.Single().SettledAmount);
            Assert.Equal(SettlementStatus.FullySettled, persisted.Single().SettlementStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 内税明細単位の売上明細行を直接指定した明細入金で税込金額が消込完了になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerLine, TaxUnit.Line);

            const string slip = "__TSTSAL_STL08";
            await InsertSalesAsync(dbContext, NewDetailSalesLine(CustomerLine, slip, 1, amount: 2750m, taxAmount: 204m));

            await InsertDetailReceiptAsync(dbContext,
                NewDirectDetailReceiptLine(CustomerLine, "__TSTDRC_STL08", 1, slip, 1, 2750m));

            await service.RecalculateForBillingGroupAsync(CustomerLine);

            var persisted = await ReloadSalesAsync(dbContext, slip);
            Assert.Equal(SettlementStatus.FullySettled, persisted.Single().SettlementStatus);
            Assert.Equal(2750m, persisted.Single().SettledAmount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 明細請求書を指定した明細入金は請求書に含まれる売上明細行へ古い順に配分される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerLine, TaxUnit.Line);

            const string slipA = "__TSTSAL_STL09A";
            const string slipB = "__TSTSAL_STL09B";
            await InsertSalesAsync(dbContext,
                NewDetailSalesLine(CustomerLine, slipA, 1, amount: 6000m, taxAmount: 444m, slipDate: new DateOnly(2026, 7, 1)),
                NewDetailSalesLine(CustomerLine, slipB, 1, amount: 4000m, taxAmount: 296m, slipDate: new DateOnly(2026, 7, 2)));

            await InsertDetailInvoiceAsync(dbContext, "__TSTDIV_STL09", CustomerLine);
            await InsertDetailInvoiceSalesLineAsync(dbContext, "__TSTDIV_STL09", slipA, 1);
            await InsertDetailInvoiceSalesLineAsync(dbContext, "__TSTDIV_STL09", slipB, 1);

            await InsertDetailReceiptAsync(dbContext,
                NewInvoiceDetailReceiptLine(CustomerLine, "__TSTDRC_STL09", 1, "__TSTDIV_STL09", 6000m));

            await service.RecalculateForBillingGroupAsync(CustomerLine);

            var salesA = await ReloadSalesAsync(dbContext, slipA);
            var salesB = await ReloadSalesAsync(dbContext, slipB);
            Assert.Equal(SettlementStatus.FullySettled, salesA.Single().SettlementStatus);
            Assert.Equal(SettlementStatus.Unsettled, salesB.Single().SettlementStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 同じ売上明細行に直接指定と明細請求書経由の入金があると直接指定分を先に充当してから残額へ配分する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerLine, TaxUnit.Line);

            const string slip = "__TSTSAL_STL10";
            await InsertSalesAsync(dbContext, NewDetailSalesLine(CustomerLine, slip, 1, amount: 10000m, taxAmount: 740m));

            await InsertDetailInvoiceAsync(dbContext, "__TSTDIV_STL10", CustomerLine);
            await InsertDetailInvoiceSalesLineAsync(dbContext, "__TSTDIV_STL10", slip, 1);

            await InsertDetailReceiptAsync(dbContext,
                NewDirectDetailReceiptLine(CustomerLine, "__TSTDRC_STL10A", 1, slip, 1, 4000m),
                NewInvoiceDetailReceiptLine(CustomerLine, "__TSTDRC_STL10B", 1, "__TSTDIV_STL10", 6000m));

            await service.RecalculateForBillingGroupAsync(CustomerLine);

            var persisted = await ReloadSalesAsync(dbContext, slip);
            Assert.Equal(SettlementStatus.FullySettled, persisted.Single().SettlementStatus);
            Assert.Equal(10000m, persisted.Single().SettledAmount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 取消済みの入金行は配分に含まれない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice);
            await InsertBillingAsync(dbContext, "__TSTBIL_STL11", CustomerInvoice, TaxUnit.Invoice, currentBillingAmount: 10000m);

            const string slip = "__TSTSAL_STL11";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, 10000m, "__TSTBIL_STL11"));

            var receipt = NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL11", 1, 10000m);
            receipt.IsDeleted = true;
            await InsertReceiptAsync(dbContext, receipt);

            var allocation = NewReceiptAllocationLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL11", 1, "__TSTBIL_STL11", 10000m);
            allocation.IsDeleted = true;
            await InsertReceiptAllocationAsync(dbContext, allocation);

            await service.RecalculateForBillingGroupAsync(CustomerInvoice);

            var persisted = await ReloadSalesAsync(dbContext, slip);
            Assert.Equal(SettlementStatus.Unsettled, persisted.Single().SettlementStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 登録と訂正と取消を続けて行ってもキャッシュ列が実態と一致し続ける()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice);
            await InsertBillingAsync(dbContext, "__TSTBIL_STL12", CustomerInvoice, TaxUnit.Invoice, currentBillingAmount: 10000m);

            const string slip = "__TSTSAL_STL12";
            const string receiptSlip = "__TSTRCP_STL12";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, 10000m, "__TSTBIL_STL12"));

            // 登録: 全額入金 → 消込完了。
            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, receiptSlip, 1, 10000m));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerInvoice, TaxUnit.Invoice, receiptSlip, 1, "__TSTBIL_STL12", 10000m));
            await service.RecalculateForBillingGroupAsync(CustomerInvoice);
            var afterCreate = await ReloadSalesAsync(dbContext, slip);
            Assert.Equal(SettlementStatus.FullySettled, afterCreate.Single().SettlementStatus);
            Assert.Equal(10000m, afterCreate.Single().SettledAmount);

            // 訂正: 入金額を6,000に減らす → 一部消込。
            var receiptLine = await dbContext.Receipts.SingleAsync(r => r.ReceiptSlipNumber == receiptSlip && r.LineNumber == 1);
            receiptLine.Amount = 6000m;
            var allocationLine = await dbContext.ReceiptAllocations.SingleAsync(a => a.ReceiptSlipNumber == receiptSlip && a.LineNumber == 1);
            allocationLine.AllocatedAmount = 6000m;
            await dbContext.SaveChangesAsync();
            await service.RecalculateForBillingGroupAsync(CustomerInvoice);
            var afterUpdate = await ReloadSalesAsync(dbContext, slip);
            Assert.Equal(SettlementStatus.PartiallySettled, afterUpdate.Single().SettlementStatus);
            Assert.Equal(6000m, afterUpdate.Single().SettledAmount);

            // 取消: 入金行を論理削除 → 未消込に戻る。
            receiptLine.IsDeleted = true;
            allocationLine.IsDeleted = true;
            await dbContext.SaveChangesAsync();
            await service.RecalculateForBillingGroupAsync(CustomerInvoice);
            var afterCancel = await ReloadSalesAsync(dbContext, slip);
            Assert.Equal(SettlementStatus.Unsettled, afterCancel.Single().SettlementStatus);
            Assert.Equal(0m, afterCancel.Single().SettledAmount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 再計算を二度実行すると二度目は一行も更新しない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerInvoice, TaxUnit.Invoice);
            await InsertBillingAsync(dbContext, "__TSTBIL_STL13", CustomerInvoice, TaxUnit.Invoice, currentBillingAmount: 10000m);

            const string slip = "__TSTSAL_STL13";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerInvoice, TaxUnit.Invoice, slip, 1, 10000m, "__TSTBIL_STL13"));
            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL13", 1, 10000m));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerInvoice, TaxUnit.Invoice, "__TSTRCP_STL13", 1, "__TSTBIL_STL13", 10000m));

            var first = await service.RecalculateForBillingGroupAsync(CustomerInvoice);
            var second = await service.RecalculateForBillingGroupAsync(CustomerInvoice);

            Assert.True(first.UpdatedSalesLineCount > 0);
            Assert.Equal(0, second.UpdatedSalesLineCount);
            Assert.Equal(0, second.UpdatedReceiptLineCount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 明示トランザクションの外で呼び出すと例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (_, service) = Resolve(scope);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecalculateForBillingGroupAsync(CustomerInvoice));
    }

    [Fact]
    public async Task 存在しない得意先コードを指定すると業務例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await Assert.ThrowsAsync<SettlementException>(
                () => service.RecalculateForBillingGroupAsync("__TSTSTL_NONE"));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    // 親子請求（請求集約）。請求集約先（Root）と請求集約元（Child）は
    // closing_day・tax_unit・rounding_type が一致していなければならないため、いずれも
    // InsertCustomerAsync の既定値（ClosingDay=15・RoundingType.Floor）のまま組む。
    private const string CustomerBillingRoot = "__TSTSTLP1";
    private const string CustomerBillingChild = "__TSTSTLC1";

    [Fact]
    public async Task 請求集約元の売上は請求集約先への入金で消し込まれる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerBillingRoot, TaxUnit.Invoice);
            await InsertCustomerAsync(dbContext, CustomerBillingChild, TaxUnit.Invoice, billingCustomerCode: CustomerBillingRoot);
            await InsertBillingAsync(dbContext, "__TSTBIL_STLG1", CustomerBillingRoot, TaxUnit.Invoice, currentBillingAmount: 11000m);

            const string rootSlip = "__TSTSAL_STLG1R";
            const string childSlip = "__TSTSAL_STLG1C";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerBillingRoot, TaxUnit.Invoice, rootSlip, 1, 6000m, "__TSTBIL_STLG1", productCode: "1001"),
                NewClosingSalesLine(CustomerBillingChild, TaxUnit.Invoice, childSlip, 1, 4000m, "__TSTBIL_STLG1", productCode: "1001"));

            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerBillingRoot, TaxUnit.Invoice, "__TSTRCP_STLG1", 1, 11000m, bankAccountCode: "1"));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerBillingRoot, TaxUnit.Invoice, "__TSTRCP_STLG1", 1, "__TSTBIL_STLG1", 11000m));

            // 請求集約元（Child）のコードで呼んでも、グループ（Root＋Child）が解決されて
            // 両方の売上が消込完了になることを確認する（起点がどちらでも同じ結果になる証明）。
            var result = await service.RecalculateForBillingGroupAsync(CustomerBillingChild);

            var rootSales = await ReloadSalesAsync(dbContext, rootSlip);
            var childSales = await ReloadSalesAsync(dbContext, childSlip);
            Assert.Equal(SettlementStatus.FullySettled, rootSales.Single().SettlementStatus);
            Assert.Equal(6000m, rootSales.Single().SettledAmount);
            Assert.Equal(SettlementStatus.FullySettled, childSales.Single().SettlementStatus);
            Assert.Equal(4000m, childSales.Single().SettledAmount);
            Assert.Equal(2, result.UpdatedSalesLineCount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 請求集約グループ内では伝票日付の古い順に配分される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerBillingRoot, TaxUnit.Invoice);
            await InsertCustomerAsync(dbContext, CustomerBillingChild, TaxUnit.Invoice, billingCustomerCode: CustomerBillingRoot);
            await InsertBillingAsync(dbContext, "__TSTBIL_STLG2", CustomerBillingRoot, TaxUnit.Invoice, currentBillingAmount: 10000m);

            const string rootSlip = "__TSTSAL_STLG2R";
            const string childSlip = "__TSTSAL_STLG2C";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerBillingRoot, TaxUnit.Invoice, rootSlip, 1, 6000m, "__TSTBIL_STLG2", slipDate: new DateOnly(2026, 7, 2), productCode: "1001"),
                NewClosingSalesLine(CustomerBillingChild, TaxUnit.Invoice, childSlip, 1, 4000m, "__TSTBIL_STLG2", slipDate: new DateOnly(2026, 7, 1), productCode: "1001"));

            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerBillingRoot, TaxUnit.Invoice, "__TSTRCP_STLG2", 1, 5500m, bankAccountCode: "1"));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerBillingRoot, TaxUnit.Invoice, "__TSTRCP_STLG2", 1, "__TSTBIL_STLG2", 5500m));

            await service.RecalculateForBillingGroupAsync(CustomerBillingRoot);

            // 請求集約元（07/01・古い）が請求集約先（07/02）より先に全額充当され、不足分は
            // 新しい行に残る。
            var childSales = await ReloadSalesAsync(dbContext, childSlip);
            var rootSales = await ReloadSalesAsync(dbContext, rootSlip);
            Assert.Equal(4000m, childSales.Single().SettledAmount);
            Assert.Equal(SettlementStatus.FullySettled, childSales.Single().SettlementStatus);
            Assert.Equal(1500m, rootSales.Single().SettledAmount);
            Assert.Equal(SettlementStatus.PartiallySettled, rootSales.Single().SettlementStatus);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 請求集約元の返品行はグループ全体が全額入金されたときだけ消込完了になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerBillingRoot, TaxUnit.Invoice);
            await InsertCustomerAsync(dbContext, CustomerBillingChild, TaxUnit.Invoice, billingCustomerCode: CustomerBillingRoot);

            // 請求A: 全額入金（8,000）→ 返品行を含めて全行が消込完了になる。
            await InsertBillingAsync(dbContext, "__TSTBIL_STLG3A", CustomerBillingRoot, TaxUnit.Invoice, currentBillingAmount: 8000m);
            const string rootSlipA = "__TSTSAL_STLG3AR";
            const string childSlipA = "__TSTSAL_STLG3AC";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerBillingRoot, TaxUnit.Invoice, rootSlipA, 1, 10000m, "__TSTBIL_STLG3A", productCode: "1001"),
                NewClosingSalesLine(CustomerBillingChild, TaxUnit.Invoice, childSlipA, 1, -2000m, "__TSTBIL_STLG3A", SlipType.Return, productCode: "1001"));
            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerBillingRoot, TaxUnit.Invoice, "__TSTRCP_STLG3A", 1, 8000m, bankAccountCode: "1"));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerBillingRoot, TaxUnit.Invoice, "__TSTRCP_STLG3A", 1, "__TSTBIL_STLG3A", 8000m));

            // 請求B: 不足入金（5,000）→ 正の売上行だけ一部消込、返品行は未消込のまま残る。
            await InsertBillingAsync(dbContext, "__TSTBIL_STLG3B", CustomerBillingRoot, TaxUnit.Invoice, currentBillingAmount: 8000m, closingYearMonth: "202608");
            const string rootSlipB = "__TSTSAL_STLG3BR";
            const string childSlipB = "__TSTSAL_STLG3BC";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerBillingRoot, TaxUnit.Invoice, rootSlipB, 1, 10000m, "__TSTBIL_STLG3B", productCode: "1001"),
                NewClosingSalesLine(CustomerBillingChild, TaxUnit.Invoice, childSlipB, 1, -2000m, "__TSTBIL_STLG3B", SlipType.Return, productCode: "1001"));
            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerBillingRoot, TaxUnit.Invoice, "__TSTRCP_STLG3B", 1, 5000m, bankAccountCode: "1"));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerBillingRoot, TaxUnit.Invoice, "__TSTRCP_STLG3B", 1, "__TSTBIL_STLG3B", 5000m));

            await service.RecalculateForBillingGroupAsync(CustomerBillingRoot);

            var rootSalesA = await ReloadSalesAsync(dbContext, rootSlipA);
            var childSalesA = await ReloadSalesAsync(dbContext, childSlipA);
            Assert.Equal(SettlementStatus.FullySettled, rootSalesA.Single().SettlementStatus);
            Assert.Equal(SettlementStatus.FullySettled, childSalesA.Single().SettlementStatus);
            Assert.Equal(-2000m, childSalesA.Single().SettledAmount);

            var rootSalesB = await ReloadSalesAsync(dbContext, rootSlipB);
            var childSalesB = await ReloadSalesAsync(dbContext, childSlipB);
            Assert.Equal(SettlementStatus.PartiallySettled, rootSalesB.Single().SettlementStatus);
            Assert.Equal(5000m, rootSalesB.Single().SettledAmount);
            Assert.Equal(SettlementStatus.Unsettled, childSalesB.Single().SettlementStatus);
            Assert.Equal(0m, childSalesB.Single().SettledAmount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 請求集約元の未請求売上は請求集約先の入金があっても未消込のまま()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerBillingRoot, TaxUnit.Invoice);
            await InsertCustomerAsync(dbContext, CustomerBillingChild, TaxUnit.Invoice, billingCustomerCode: CustomerBillingRoot);
            await InsertBillingAsync(dbContext, "__TSTBIL_STLG4", CustomerBillingRoot, TaxUnit.Invoice, currentBillingAmount: 10000m);

            const string rootSlip = "__TSTSAL_STLG4R";
            const string childSlip = "__TSTSAL_STLG4C";
            await InsertSalesAsync(dbContext,
                NewClosingSalesLine(CustomerBillingRoot, TaxUnit.Invoice, rootSlip, 1, 10000m, "__TSTBIL_STLG4", productCode: "1001"),
                // 請求集約元の未請求売上（billing_number=null）。
                NewClosingSalesLine(CustomerBillingChild, TaxUnit.Invoice, childSlip, 1, 3000m, null, productCode: "1001"));

            await InsertReceiptAsync(dbContext,
                NewReceiptLine(CustomerBillingRoot, TaxUnit.Invoice, "__TSTRCP_STLG4", 1, 10000m, bankAccountCode: "1"));
            await InsertReceiptAllocationAsync(dbContext,
                NewReceiptAllocationLine(CustomerBillingRoot, TaxUnit.Invoice, "__TSTRCP_STLG4", 1, "__TSTBIL_STLG4", 10000m));

            await service.RecalculateForBillingGroupAsync(CustomerBillingRoot);

            var rootSales = await ReloadSalesAsync(dbContext, rootSlip);
            var childSales = await ReloadSalesAsync(dbContext, childSlip);
            Assert.Equal(SettlementStatus.FullySettled, rootSales.Single().SettlementStatus);
            Assert.Equal(SettlementStatus.Unsettled, childSales.Single().SettlementStatus);
            Assert.Equal(0m, childSales.Single().SettledAmount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static (BmcsDbContext DbContext, SettlementService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<SettlementService>());

    private static Task InsertCustomerAsync(
        BmcsDbContext dbContext, string customerCode, TaxUnit taxUnit, string? billingCustomerCode = null)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new CustomerEntity
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = taxUnit == TaxUnit.Line ? (byte)0 : (byte)15,
            TaxUnit = taxUnit,
            RoundingType = RoundingType.Floor,
            BillingCustomerCode = billingCustomerCode ?? customerCode,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static Task InsertBillingAsync(
        BmcsDbContext dbContext, string billingNumber, string customerCode, TaxUnit taxUnit,
        decimal currentBillingAmount, string closingYearMonth = "202607")
    {
        var now = DateTime.Now;
        dbContext.Billings.Add(new BillingEntity
        {
            BillingNumber = billingNumber,
            CustomerCode = customerCode,
            TaxUnit = taxUnit,
            CustomerName = "テスト用得意先",
            BillingDate = new DateOnly(2026, 7, 20),
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

    private static Task InsertReceiptAllocationAsync(BmcsDbContext dbContext, params ReceiptAllocationEntity[] lines)
    {
        dbContext.ReceiptAllocations.AddRange(lines);
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

    private static Task<List<SalesEntity>> ReloadSalesAsync(BmcsDbContext dbContext, string salesSlipNumber) =>
        dbContext.Sales.AsNoTracking()
            .Where(s => s.SalesSlipNumber == salesSlipNumber)
            .ToListAsync();

    private static SalesEntity NewClosingSalesLine(
        string customerCode, TaxUnit taxUnit, string slipNumber, short lineNumber, decimal amount,
        string? billingNumber, SlipType slipType = SlipType.Sales, DateOnly? slipDate = null,
        string productCode = "PRD001")
    {
        var now = DateTime.Now;
        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = lineNumber,
            SlipDate = slipDate ?? new DateOnly(2026, 7, 1),
            CustomerCode = customerCode,
            TaxUnit = taxUnit,
            CustomerName = "テスト用得意先",
            SlipType = slipType,
            ProductCode = productCode,
            ProductName = "テスト用商品",
            Quantity = 1m,
            UnitPrice = Math.Abs(amount),
            Amount = amount,
            CostPrice = 0m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            SlipTaxAmount = taxUnit == TaxUnit.Slip ? 0m : null,
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

    private static SalesEntity NewDetailSalesLine(
        string customerCode, string slipNumber, short lineNumber, decimal amount, decimal taxAmount,
        DateOnly? slipDate = null)
    {
        var now = DateTime.Now;
        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = lineNumber,
            SlipDate = slipDate ?? new DateOnly(2026, 7, 1),
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
            TaxAmount = taxAmount,
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

    /// <summary>締め入金の明細行（支払手段の内訳）。充当は別途<see cref="NewReceiptAllocationLine"/>で作る。</summary>
    private static ReceiptEntity NewReceiptLine(
        string customerCode, TaxUnit taxUnit, string receiptSlipNumber, short lineNumber, decimal amount,
        string bankAccountCode = "BNK001")
    {
        var now = DateTime.Now;
        return new ReceiptEntity
        {
            ReceiptSlipNumber = receiptSlipNumber,
            LineNumber = lineNumber,
            ReceiptDate = new DateOnly(2026, 7, 25),
            CustomerCode = customerCode,
            TaxUnit = taxUnit,
            CustomerName = "テスト用得意先",
            DepositMethodCode = "TRANSFER",
            BankAccountCode = bankAccountCode,
            Amount = amount,
            AllocationStatus = AllocationStatus.Unallocated,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    /// <summary>締め入金の請求への充当（内部データ）。</summary>
    private static ReceiptAllocationEntity NewReceiptAllocationLine(
        string customerCode, TaxUnit taxUnit, string receiptSlipNumber, short lineNumber,
        string? billingNumber, decimal allocatedAmount, decimal feeAdjustmentAmount = 0m)
    {
        var now = DateTime.Now;
        return new ReceiptAllocationEntity
        {
            ReceiptSlipNumber = receiptSlipNumber,
            LineNumber = lineNumber,
            CustomerCode = customerCode,
            TaxUnit = taxUnit,
            BillingNumber = billingNumber,
            AllocatedAmount = allocatedAmount,
            FeeAdjustmentAmount = feeAdjustmentAmount,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    private static DetailReceiptEntity NewDirectDetailReceiptLine(
        string customerCode, string detailReceiptNumber, short lineNumber,
        string targetSalesSlipNumber, short targetSalesLineNumber, decimal allocatedAmount)
    {
        var now = DateTime.Now;
        return new DetailReceiptEntity
        {
            DetailReceiptNumber = detailReceiptNumber,
            LineNumber = lineNumber,
            ReceiptDate = new DateOnly(2026, 7, 25),
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            DepositMethodCode = "CASH",
            ReceiptAmount = allocatedAmount,
            TargetType = DetailReceiptTargetType.SalesLine,
            TargetSalesSlipNumber = targetSalesSlipNumber,
            TargetSalesLineNumber = targetSalesLineNumber,
            TargetDetailInvoiceNumber = null,
            AllocatedAmount = allocatedAmount,
            FeeAdjustmentAmount = 0m,
            AllocationStatus = AllocationStatus.Unallocated,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    private static DetailReceiptEntity NewInvoiceDetailReceiptLine(
        string customerCode, string detailReceiptNumber, short lineNumber,
        string targetDetailInvoiceNumber, decimal allocatedAmount)
    {
        var now = DateTime.Now;
        return new DetailReceiptEntity
        {
            DetailReceiptNumber = detailReceiptNumber,
            LineNumber = lineNumber,
            ReceiptDate = new DateOnly(2026, 7, 22),
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            DepositMethodCode = "TRANSFER",
            ReceiptAmount = allocatedAmount,
            TargetType = DetailReceiptTargetType.DetailInvoice,
            TargetSalesSlipNumber = null,
            TargetSalesLineNumber = null,
            TargetDetailInvoiceNumber = targetDetailInvoiceNumber,
            AllocatedAmount = allocatedAmount,
            FeeAdjustmentAmount = 0m,
            AllocationStatus = AllocationStatus.Unallocated,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }
}
