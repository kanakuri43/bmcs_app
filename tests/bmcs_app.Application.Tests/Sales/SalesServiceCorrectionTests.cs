using bmcs_app.Application.Common;
using bmcs_app.Application.Order;
using bmcs_app.Application.Sales;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Sales;

/// <summary>
/// 受注からの売上確定（TODO.md 5-3）・返品値引（5-4）・訂正取消（5-6）の結合テスト。
/// 開発用ライブDBに対して実行する（docs/architecture.md 16章）。
/// </summary>
/// <remarks>
/// <see cref="SalesService.CreateAsync"/>／<see cref="SalesService.UpdateAsync"/>／
/// <see cref="SalesService.CancelSlipAsync"/> はいずれも内部で独自に
/// <c>BeginTransactionAsync</c>→<c>SaveChangesAsync</c>→<c>CommitAsync</c> を行うため、
/// <c>SalesServiceTests</c>と同じ「外側をトランザクションで包みRollbackする」方式は使えない
/// （ネストした<c>BeginTransactionAsync</c>はEF Coreが例外を投げる）。使い捨てデータ
/// （<c>__TESTORD*</c> の受注、業務キーと衝突しない売上番号）をコミットし、
/// <c>finally</c>で物理削除する（M-17は業務操作の話であり、テストの後始末は例外扱い。
/// <c>SalesServiceTests</c>の既存方針と同じ）。
/// </remarks>
public class SalesServiceCorrectionTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerCode = "CUS001";
    private const string ProductCode = "PRD001";

    [Fact]
    public async Task 受注から一部数量を売上確定すると一部売上になり残数量が残る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);
        const string orderSlipNumber = "__TESTORD_CONF01";

        await InsertOrderLineAsync(dbContext, orderSlipNumber, orderQuantity: 10m);

        var line = NewSalesLine(quantity: 4m, orderSlipNumber: orderSlipNumber, orderLineNumber: 1);
        string? salesSlipNumber = null;
        try
        {
            salesSlipNumber = await salesService.CreateAsync([line], RoundingType.Floor);

            var order = await ReloadOrderAsync(dbContext, orderSlipNumber);
            Assert.Equal(4m, order.SalesConfirmedQuantity);
            Assert.Equal(OrderStatus.PartiallySold, order.OrderStatus);
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber, orderSlipNumber);
        }
    }

    [Fact]
    public async Task 受注数量を超える売上確定は拒否され売上行も登録されない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);
        const string orderSlipNumber = "__TESTORD_CONF02";

        await InsertOrderLineAsync(dbContext, orderSlipNumber, orderQuantity: 10m);

        var line = NewSalesLine(quantity: 11m, orderSlipNumber: orderSlipNumber, orderLineNumber: 1);
        try
        {
            await Assert.ThrowsAsync<OrderOperationException>(() => salesService.CreateAsync([line], RoundingType.Floor));

            var order = await ReloadOrderAsync(dbContext, orderSlipNumber);
            Assert.Equal(0m, order.SalesConfirmedQuantity); // ロールバックされ受注側も変化しない

            var persistedCount = await dbContext.Sales.CountAsync(s => s.OrderSlipNumber == orderSlipNumber);
            Assert.Equal(0, persistedCount); // 売上行も登録されない
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber: null, orderSlipNumber);
        }
    }

    [Fact]
    public async Task 返品行はマイナス数量マイナス金額で登録できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);

        var line = NewSalesLine(quantity: -2m, slipType: SlipType.Return);
        string? salesSlipNumber = null;
        try
        {
            salesSlipNumber = await salesService.CreateAsync([line], RoundingType.Floor);

            var persisted = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == salesSlipNumber);
            Assert.Equal(SlipType.Return, persisted.SlipType);
            Assert.Equal(-2m, persisted.Quantity);
            Assert.True(persisted.Amount < 0m);
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber, orderSlipNumber: null);
        }
    }

    [Fact]
    public async Task 数量と金額の符号が矛盾すると拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);

        var line = NewSalesLine(quantity: -2m, slipType: SlipType.Return);
        line.Amount = 2000m; // 数量はマイナスだが金額はプラスのまま（矛盾データ）

        await Assert.ThrowsAsync<SalesOperationException>(() => salesService.CreateAsync([line], RoundingType.Floor));

        var persistedCount = await dbContext.Sales.CountAsync(s => s.SalesSlipNumber == line.SalesSlipNumber);
        Assert.Equal(0, persistedCount);
    }

    [Fact]
    public async Task 返品値引行を受注に紐付けると拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);
        const string orderSlipNumber = "__TESTORD_CONF03";

        await InsertOrderLineAsync(dbContext, orderSlipNumber, orderQuantity: 10m);

        var line = NewSalesLine(quantity: -2m, slipType: SlipType.Return, orderSlipNumber: orderSlipNumber, orderLineNumber: 1);
        try
        {
            await Assert.ThrowsAsync<SalesOperationException>(() => salesService.CreateAsync([line], RoundingType.Floor));
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber: null, orderSlipNumber);
        }
    }

    [Fact]
    public async Task 訂正で数量を増やすと税額が再計算され受注側の数量も追随する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);
        const string orderSlipNumber = "__TESTORD_CORR01";
        await InsertOrderLineAsync(dbContext, orderSlipNumber, orderQuantity: 10m);

        var createLine = NewSalesLine(quantity: 3m, orderSlipNumber: orderSlipNumber, orderLineNumber: 1);
        string? salesSlipNumber = null;
        try
        {
            salesSlipNumber = await salesService.CreateAsync([createLine], RoundingType.Floor);

            var loaded = await dbContext.Sales.Where(s => s.SalesSlipNumber == salesSlipNumber).ToListAsync();
            var loadedLineNumbers = loaded.Select(l => l.LineNumber).ToList();

            var updated = NewSalesLine(quantity: 5m, orderSlipNumber: orderSlipNumber, orderLineNumber: 1);
            updated.LineNumber = loaded[0].LineNumber;

            await salesService.UpdateAsync(salesSlipNumber, [updated], RoundingType.Floor, loadedLineNumbers);

            var persisted = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == salesSlipNumber);
            Assert.Equal(5m, persisted.Quantity);

            var order = await ReloadOrderAsync(dbContext, orderSlipNumber);
            Assert.Equal(5m, order.SalesConfirmedQuantity);
            Assert.Equal(OrderStatus.PartiallySold, order.OrderStatus);
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber, orderSlipNumber);
        }
    }

    [Fact]
    public async Task 訂正で行を削除すると論理削除され受注側が逆遷移する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);
        const string orderSlipNumber = "__TESTORD_CORR02";
        await InsertOrderLineAsync(dbContext, orderSlipNumber, orderQuantity: 10m);

        var createLine = NewSalesLine(quantity: 4m, orderSlipNumber: orderSlipNumber, orderLineNumber: 1);
        string? salesSlipNumber = null;
        try
        {
            salesSlipNumber = await salesService.CreateAsync([createLine], RoundingType.Floor);
            var loadedLineNumbers = await dbContext.Sales
                .Where(s => s.SalesSlipNumber == salesSlipNumber)
                .Select(s => s.LineNumber)
                .ToListAsync();

            // 明細行を1件も残さない（全行削除＝実質的な取消）
            await salesService.UpdateAsync(salesSlipNumber, [], RoundingType.Floor, loadedLineNumbers);

            var persisted = await dbContext.Sales.AsNoTracking()
                .Where(s => s.SalesSlipNumber == salesSlipNumber)
                .ToListAsync();
            Assert.All(persisted, l => Assert.True(l.IsDeleted));

            var order = await ReloadOrderAsync(dbContext, orderSlipNumber);
            Assert.Equal(0m, order.SalesConfirmedQuantity);
            Assert.Equal(OrderStatus.NotSold, order.OrderStatus);
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber, orderSlipNumber);
        }
    }

    [Fact]
    public async Task 一部消込の売上を訂正して金額を減らすと消込済金額が新しい金額に丸められる()
    {
        // TODO.md 7-1レビューで発見した既存不整合の修正確認: UpdateAsyncは消込完了(3)のみを
        // 編集ロック対象にし一部消込(2)は編集を許すため、金額を減らす訂正でsettled_amountが
        // 新しいamountを超えて取り残る経路があった。SettlementService.RecalculateForCustomerAsync
        // を配線したことで、実際の入金データ（detail_receipt）に基づき新しい金額へ丸め直される
        // ことを確認する。内税明細単位（都度得意先）はbilling_numberを持たないため、
        // 一部消込のままUpdateAsyncの編集ロックに引っかからない（CUS001は請求単位で
        // billing_numberが付くと編集ロック条件1に該当するため、この検証には使えない）。
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);

        const string testCustomerCode = "__TSTCOR1";
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = testCustomerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = 0,
            TaxUnit = TaxUnit.Line,
            RoundingType = RoundingType.Floor,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        await dbContext.SaveChangesAsync();

        var createLine = new SalesEntity
        {
            SalesSlipNumber = string.Empty,
            LineNumber = 1,
            SlipDate = DateOnly.FromDateTime(DateTime.Today),
            CustomerCode = testCustomerCode,
            TaxUnit = TaxUnit.Line,
            CustomerName = "テスト用得意先",
            SlipType = SlipType.Sales,
            ProductCode = ProductCode,
            ProductName = "テスト用商品",
            Quantity = 6m,
            UnitPrice = 1000m,
            Amount = 6000m,
            CostPrice = 700m,
            TaxCategory = TaxCategory.Reduced,
            TaxRate = 8m,
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Unbilled,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };

        string? salesSlipNumber = null;
        try
        {
            salesSlipNumber = await salesService.CreateAsync([createLine], RoundingType.Floor);
            var loadedLineNumbers = await dbContext.Sales
                .Where(s => s.SalesSlipNumber == salesSlipNumber)
                .Select(s => s.LineNumber)
                .ToListAsync();

            // 実際の明細入金（都度得意先・直接指定）で5,500円分を消込済にする（一部消込）。
            dbContext.DetailReceipts.Add(new DetailReceipt
            {
                DetailReceiptNumber = "__TSTCOR_DRC01",
                LineNumber = 1,
                ReceiptDate = DateOnly.FromDateTime(DateTime.Today),
                CustomerCode = testCustomerCode,
                CustomerName = "テスト用得意先",
                DepositMethodCode = "CASH",
                ReceiptAmount = 5500m,
                TargetType = DetailReceiptTargetType.SalesLine,
                TargetSalesSlipNumber = salesSlipNumber,
                TargetSalesLineNumber = loadedLineNumbers[0],
                AllocatedAmount = 5500m,
                FeeAdjustmentAmount = 0m,
                AllocationStatus = AllocationStatus.Unallocated,
                CreatedBy = "TEST",
                CreatedAt = now,
                UpdatedBy = "TEST",
                UpdatedAt = now,
            });
            await dbContext.SaveChangesAsync();

            var updated = new SalesEntity
            {
                SalesSlipNumber = salesSlipNumber,
                LineNumber = loadedLineNumbers[0],
                SlipDate = createLine.SlipDate,
                CustomerCode = testCustomerCode,
                TaxUnit = TaxUnit.Line,
                CustomerName = "テスト用得意先",
                SlipType = SlipType.Sales,
                ProductCode = ProductCode,
                ProductName = "テスト用商品",
                Quantity = 4m,
                UnitPrice = 1000m,
                Amount = 4000m,
                CostPrice = 700m,
                TaxCategory = TaxCategory.Reduced,
                TaxRate = 8m,
                DeliveryNoteIssueCount = 0,
                BillingStatus = BillingLinkStatus.Unbilled,
                SettlementStatus = SettlementStatus.Unsettled,
                SettledAmount = 0m,
                CreatedBy = "TEST",
                CreatedAt = now,
                UpdatedBy = "TEST",
                UpdatedAt = now,
            };

            await salesService.UpdateAsync(salesSlipNumber, [updated], RoundingType.Floor, loadedLineNumbers);

            var persisted = await dbContext.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == salesSlipNumber);
            // 実際の入金5,500円は新しい金額4,000円を上回るため、消込済金額は4,000円に
            // 丸められ消込完了になる（訂正前の5,500円のまま取り残らない）。
            Assert.Equal(4000m, persisted.SettledAmount);
            Assert.Equal(SettlementStatus.FullySettled, persisted.SettlementStatus);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.detail_receipt WHERE detail_receipt_number = {"__TSTCOR_DRC01"}");
            if (salesSlipNumber is not null)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM dbo.sales WHERE sales_slip_number = {salesSlipNumber}");
            }

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.customer WHERE customer_code = {testCustomerCode}");
        }
    }

    [Fact]
    public async Task 請求締め済みの売上は訂正できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);

        var createLine = NewSalesLine(quantity: 1m);
        string? salesSlipNumber = null;
        try
        {
            salesSlipNumber = await salesService.CreateAsync([createLine], RoundingType.Floor);

            await dbContext.Sales
                .Where(s => s.SalesSlipNumber == salesSlipNumber)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.BillingNumber, "BIL_INV001"));

            // ExecuteUpdateAsync はトラッカーを経由しないため、CreateAsync直後にトラッキングされた
            // インスタンス（rowversionが更新前のまま）をクリアし、以降のUpdateAsyncが実DBの
            // 最新状態を素直に読み直せるようにする（さもないと本来無関係のはずの排他エラーになる）。
            dbContext.ChangeTracker.Clear();

            var loadedLineNumbers = await dbContext.Sales
                .Where(s => s.SalesSlipNumber == salesSlipNumber)
                .Select(s => s.LineNumber)
                .ToListAsync();
            var updated = NewSalesLine(quantity: 2m);
            updated.LineNumber = loadedLineNumbers[0];

            await Assert.ThrowsAsync<SalesOperationException>(
                () => salesService.UpdateAsync(salesSlipNumber, [updated], RoundingType.Floor, loadedLineNumbers));
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber, orderSlipNumber: null);
        }
    }

    [Fact]
    public async Task 消込完了の売上は訂正できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);

        var createLine = NewSalesLine(quantity: 1m);
        string? salesSlipNumber = null;
        try
        {
            salesSlipNumber = await salesService.CreateAsync([createLine], RoundingType.Floor);

            await dbContext.Sales
                .Where(s => s.SalesSlipNumber == salesSlipNumber)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.SettlementStatus, SettlementStatus.FullySettled));

            dbContext.ChangeTracker.Clear();

            var loadedLineNumbers = await dbContext.Sales
                .Where(s => s.SalesSlipNumber == salesSlipNumber)
                .Select(s => s.LineNumber)
                .ToListAsync();
            var updated = NewSalesLine(quantity: 2m);
            updated.LineNumber = loadedLineNumbers[0];

            await Assert.ThrowsAsync<SalesOperationException>(
                () => salesService.UpdateAsync(salesSlipNumber, [updated], RoundingType.Floor, loadedLineNumbers));
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber, orderSlipNumber: null);
        }
    }

    [Fact]
    public async Task 月次締め確定済みの年月への訂正は拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);

        // CUS001は2026-01-31分が確定済み（seed_dev_data.sql）。その年月内の伝票日付で登録する。
        var createLine = NewSalesLine(quantity: 1m, slipDate: new DateOnly(2026, 1, 15));
        string? salesSlipNumber = null;
        try
        {
            salesSlipNumber = await salesService.CreateAsync([createLine], RoundingType.Floor);

            var loadedLineNumbers = await dbContext.Sales
                .Where(s => s.SalesSlipNumber == salesSlipNumber)
                .Select(s => s.LineNumber)
                .ToListAsync();
            var updated = NewSalesLine(quantity: 2m, slipDate: new DateOnly(2026, 1, 20));
            updated.LineNumber = loadedLineNumbers[0];

            await Assert.ThrowsAsync<SalesOperationException>(
                () => salesService.UpdateAsync(salesSlipNumber, [updated], RoundingType.Floor, loadedLineNumbers));
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber, orderSlipNumber: null);
        }
    }

    [Fact]
    public async Task 訂正後の伝票日付が確定済み月次締め年月に移動する場合は拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);

        // 登録時は未締めの年月、訂正で確定済み年月（2026-01）へ動かそうとする。
        var createLine = NewSalesLine(quantity: 1m, slipDate: new DateOnly(2026, 8, 1));
        string? salesSlipNumber = null;
        try
        {
            salesSlipNumber = await salesService.CreateAsync([createLine], RoundingType.Floor);

            var loadedLineNumbers = await dbContext.Sales
                .Where(s => s.SalesSlipNumber == salesSlipNumber)
                .Select(s => s.LineNumber)
                .ToListAsync();
            var updated = NewSalesLine(quantity: 1m, slipDate: new DateOnly(2026, 1, 20));
            updated.LineNumber = loadedLineNumbers[0];

            await Assert.ThrowsAsync<SalesOperationException>(
                () => salesService.UpdateAsync(salesSlipNumber, [updated], RoundingType.Floor, loadedLineNumbers));
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber, orderSlipNumber: null);
        }
    }

    [Fact]
    public async Task 他ユーザーが明細行を追加していた場合は排他エラーになる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);

        var createLine = NewSalesLine(quantity: 1m);
        string? salesSlipNumber = null;
        try
        {
            salesSlipNumber = await salesService.CreateAsync([createLine], RoundingType.Floor);

            var loadedLineNumbers = await dbContext.Sales
                .Where(s => s.SalesSlipNumber == salesSlipNumber)
                .Select(s => s.LineNumber)
                .ToListAsync();

            // 別ユーザーが行を追加した状況を再現する。
            var extraLine = NewSalesLine(quantity: 1m);
            extraLine.SalesSlipNumber = salesSlipNumber!;
            extraLine.LineNumber = 99;
            dbContext.Sales.Add(extraLine);
            await dbContext.SaveChangesAsync();

            var updated = NewSalesLine(quantity: 2m);
            updated.LineNumber = loadedLineNumbers[0];

            await Assert.ThrowsAsync<SlipConcurrencyException>(
                () => salesService.UpdateAsync(salesSlipNumber!, [updated], RoundingType.Floor, loadedLineNumbers));
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber, orderSlipNumber: null);
        }
    }

    [Fact]
    public async Task 取消すると全行が論理削除され受注側の数量が戻る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, _) = Resolve(scope);
        const string orderSlipNumber = "__TESTORD_CANCEL01";
        await InsertOrderLineAsync(dbContext, orderSlipNumber, orderQuantity: 10m);

        var createLine = NewSalesLine(quantity: 4m, orderSlipNumber: orderSlipNumber, orderLineNumber: 1);
        string? salesSlipNumber = null;
        try
        {
            salesSlipNumber = await salesService.CreateAsync([createLine], RoundingType.Floor);

            await salesService.CancelSlipAsync(salesSlipNumber);

            var persisted = await dbContext.Sales.AsNoTracking()
                .Where(s => s.SalesSlipNumber == salesSlipNumber)
                .ToListAsync();
            Assert.All(persisted, l => Assert.True(l.IsDeleted));

            var order = await ReloadOrderAsync(dbContext, orderSlipNumber);
            Assert.Equal(0m, order.SalesConfirmedQuantity);
            Assert.Equal(OrderStatus.NotSold, order.OrderStatus);
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber, orderSlipNumber);
        }
    }

    [Fact]
    public async Task 中止済みの受注に紐付く売上を取消すると受注は中止のまま数量だけ戻る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, salesService, orderStatusService) = Resolve(scope);
        const string orderSlipNumber = "__TESTORD_CANCEL02";
        await InsertOrderLineAsync(dbContext, orderSlipNumber, orderQuantity: 10m);

        var createLine = NewSalesLine(quantity: 4m, orderSlipNumber: orderSlipNumber, orderLineNumber: 1);
        string? salesSlipNumber = null;
        try
        {
            salesSlipNumber = await salesService.CreateAsync([createLine], RoundingType.Floor);

            // 一部売上化のあと受注を中止する（分納済みの実績はSalesConfirmedQuantityに残る）。
            await orderStatusService.CancelSlipAsync(orderSlipNumber);
            var afterCancel = await ReloadOrderAsync(dbContext, orderSlipNumber);
            Assert.Equal(OrderStatus.Cancelled, afterCancel.OrderStatus);
            Assert.Equal(4m, afterCancel.SalesConfirmedQuantity);

            // 中止後でも、既存の売上を取消（負のデルタ）できる。中止状態は維持される。
            await salesService.CancelSlipAsync(salesSlipNumber);

            var afterSalesCancel = await ReloadOrderAsync(dbContext, orderSlipNumber);
            Assert.Equal(OrderStatus.Cancelled, afterSalesCancel.OrderStatus);
            Assert.Equal(0m, afterSalesCancel.SalesConfirmedQuantity);
        }
        finally
        {
            await CleanupAsync(dbContext, salesSlipNumber, orderSlipNumber);
        }
    }

    private static (BmcsDbContext DbContext, SalesService SalesService, OrderStatusService OrderStatusService) Resolve(
        AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<SalesService>(),
        scope.ServiceProvider.GetRequiredService<OrderStatusService>());

    private static async Task InsertOrderLineAsync(BmcsDbContext dbContext, string orderSlipNumber, decimal orderQuantity)
    {
        dbContext.OrderSlips.Add(new OrderSlip
        {
            OrderSlipNumber = orderSlipNumber,
            LineNumber = 1,
            OrderDate = DateOnly.FromDateTime(DateTime.Today),
            CustomerCode = CustomerCode,
            CustomerName = "テスト用得意先",
            ProductCode = ProductCode,
            ProductName = "テスト用商品",
            OrderQuantity = orderQuantity,
            UnitPrice = 1000m,
            Amount = orderQuantity * 1000m,
            CostPrice = 700m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            AllocatedQuantity = 0m,
            OrderStatus = OrderStatus.NotSold,
            SalesConfirmedQuantity = 0m,
            CreatedBy = "TEST",
            CreatedAt = DateTime.Now,
            UpdatedBy = "TEST",
            UpdatedAt = DateTime.Now,
        });
        await dbContext.SaveChangesAsync();
    }

    private static Task<OrderSlip> ReloadOrderAsync(BmcsDbContext dbContext, string orderSlipNumber) =>
        dbContext.OrderSlips.AsNoTracking().SingleAsync(o => o.OrderSlipNumber == orderSlipNumber && o.LineNumber == 1);

    private static async Task CleanupAsync(BmcsDbContext dbContext, string? salesSlipNumber, string? orderSlipNumber)
    {
        if (salesSlipNumber is not null)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.sales WHERE sales_slip_number = {salesSlipNumber}");
        }

        if (orderSlipNumber is not null)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.order_slip WHERE order_slip_number = {orderSlipNumber}");
        }
    }

    private static SalesEntity NewSalesLine(
        decimal quantity,
        SlipType slipType = SlipType.Sales,
        string? orderSlipNumber = null,
        short? orderLineNumber = null,
        DateOnly? slipDate = null)
    {
        var unitPrice = 1000m;
        var amount = ConsumptionTaxCalculator.CalculateLineAmount(quantity, unitPrice, RoundingType.Floor);
        var now = DateTime.Now;

        return new SalesEntity
        {
            SalesSlipNumber = string.Empty,
            LineNumber = 1,
            SlipDate = slipDate ?? DateOnly.FromDateTime(DateTime.Today),
            CustomerCode = CustomerCode,
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "株式会社山田商事",
            SlipType = slipType,
            ProductCode = ProductCode,
            ProductName = "テスト用商品",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = amount,
            CostPrice = slipType == SlipType.Discount ? 0m : 700m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Unbilled,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            OrderSlipNumber = orderSlipNumber,
            OrderLineNumber = orderLineNumber,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }
}
