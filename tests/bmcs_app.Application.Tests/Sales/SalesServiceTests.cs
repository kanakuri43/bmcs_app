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
/// 売上入力（TODO.md 5-2）の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する
/// （docs/architecture.md 16章）。GUIでのsmoke testの代わりに、税区分3種すべてで
/// <see cref="SalesService.CreateAsync"/> を実際に呼び、DBのCHECK制約
/// （<c>CK_sales_tax_amount_by_tax_unit</c>／<c>CK_sales_billing_number_by_tax_unit</c>）を
/// 満たす値で登録されることを実証する。完了条件「税区分別に正しい税額で登録される」の実証。
/// </summary>
/// <remarks>
/// 登録した行は各テストの最後に物理削除する（作成した売上番号のみを対象とし、他データには
/// 触れない）。実際の業務では伝票を物理削除しない（M-17）が、本テストは検証用データの
/// 後始末であり業務操作ではないため、<see cref="DevDatabaseFixture"/> 自身のテストキー削除と
/// 同じ扱いとする。ただし採番テーブルの <c>current_value</c> は巻き戻さない（欠番は許容する。
/// M-2の欠番なし方針は正常運用時の話であり、テストで一時的に消費した番号を戻す仕組みは無い）。
/// </remarks>
public class SalesServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    [Fact]
    public async Task 請求単位の得意先は税額を持たずに登録される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var customer = await GetCustomerAsync(scope, "CUS001");
        Assert.Equal(TaxUnit.Invoice, customer.TaxUnit);

        var lines = new[]
        {
            NewLine(customer, 1, TaxCategory.Standard, 10m, quantity: 5m, unitPrice: 1000m),
            NewLine(customer, 2, TaxCategory.Reduced, 8m, quantity: 2m, unitPrice: 500m),
        };

        var slipNumber = await CreateAndCleanupAsync(scope, lines, customer.RoundingType, async persisted =>
        {
            Assert.All(persisted, l => Assert.Null(l.SlipTaxAmount));
            Assert.All(persisted, l => Assert.Null(l.TaxAmount));
            Assert.All(persisted, l => Assert.Null(l.BillingNumber));
            Assert.All(persisted, l => Assert.Equal(BillingLinkStatus.Unbilled, l.BillingStatus));
            Assert.All(persisted, l => Assert.Equal(SettlementStatus.Unsettled, l.SettlementStatus));
            Assert.All(persisted, l => Assert.Equal(0m, l.SettledAmount));
            Assert.All(persisted, l => Assert.Null(l.DeliveryNoteIssuedAt));
            Assert.All(persisted, l => Assert.Equal(0, l.DeliveryNoteIssueCount));
            Assert.All(persisted, l => Assert.Equal(SlipType.Sales, l.SlipType));
            Assert.All(persisted, l => Assert.Null(l.OrderSlipNumber));
            Assert.All(persisted, l => Assert.Null(l.OrderLineNumber));
        });

        Assert.Equal(8, slipNumber.Length);
    }

    [Fact]
    public async Task 伝票単位の得意先は伝票全体で1回だけ丸めた税額が全行に入る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var customer = await GetCustomerAsync(scope, "CUS002");
        Assert.Equal(TaxUnit.Slip, customer.TaxUnit);
        Assert.Equal(RoundingType.RoundHalfUp, customer.RoundingType);

        // 端数が出る単価: 1234×3=3702（10%）、500×1=500（8%）。
        var lines = new[]
        {
            NewLine(customer, 1, TaxCategory.Standard, 10m, quantity: 3m, unitPrice: 1234m),
            NewLine(customer, 2, TaxCategory.Reduced, 8m, quantity: 1m, unitPrice: 500m),
        };

        await CreateAndCleanupAsync(scope, lines, customer.RoundingType, async persisted =>
        {
            // 3702×10% = 370.2 → 四捨五入で370。500×8% = 40.0。合計410。
            const decimal expectedSlipTaxAmount = 410m;

            Assert.All(persisted, l => Assert.Equal(expectedSlipTaxAmount, l.SlipTaxAmount));
            Assert.All(persisted, l => Assert.Null(l.TaxAmount));
            Assert.All(persisted, l => Assert.Null(l.BillingNumber));

            // 「行数×税額」の重複計上になっていないこと（全行が同一値を持つのみで、
            // SUMすると誤って2倍になっていないか）。
            var distinctValues = persisted.Select(l => l.SlipTaxAmount).Distinct().ToList();
            Assert.Single(distinctValues);
        });
    }

    [Fact]
    public async Task 内税明細単位の得意先は行ごとに税額が確定する()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var customer = await GetCustomerAsync(scope, "CUS003");
        Assert.Equal(TaxUnit.Line, customer.TaxUnit);
        Assert.Equal(RoundingType.Ceiling, customer.RoundingType);

        // PRD002相当: 軽減8%・内税単価540・数量20 → amount 10800 → 10800×8/108 = 800（割り切れる）。
        var lines = new[]
        {
            NewLine(customer, 1, TaxCategory.Reduced, 8m, quantity: 20m, unitPrice: 540m),
        };

        await CreateAndCleanupAsync(scope, lines, customer.RoundingType, async persisted =>
        {
            var line = Assert.Single(persisted);
            Assert.Equal(10800m, line.Amount);
            Assert.Equal(800m, line.TaxAmount);
            Assert.Null(line.SlipTaxAmount);
            Assert.Null(line.BillingNumber); // tax_unit=3 は締め請求データを持たない（常にNULL）
        });
    }

    [Fact]
    public async Task 保存された伝票摘要と行摘要は仕様どおりに複写される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var customer = await GetCustomerAsync(scope, "CUS001");

        var lines = new[]
        {
            NewLine(customer, 1, TaxCategory.Standard, 10m, quantity: 1m, unitPrice: 1000m, lineRemarks: "行摘要A", slipRemarks: "伝票摘要X"),
            NewLine(customer, 2, TaxCategory.Standard, 10m, quantity: 1m, unitPrice: 2000m, lineRemarks: "行摘要B", slipRemarks: "伝票摘要X"),
        };

        await CreateAndCleanupAsync(scope, lines, customer.RoundingType, async persisted =>
        {
            Assert.All(persisted, l => Assert.Equal("伝票摘要X", l.SlipRemarks));
            Assert.Equal(["行摘要A", "行摘要B"], persisted.OrderBy(l => l.LineNumber).Select(l => l.LineRemarks));
        });
    }

    /// <summary>
    /// 得意先マスタを直接読む（<c>CustomerService</c>はロギング未構成の結合テストDIコンテナでは
    /// 解決できないため使わない。テストが必要とするのはマスタ値の読み取りだけであり、
    /// 業務ロジックを持つサービス層を経由する必要はない）。
    /// </summary>
    private static async Task<Customer> GetCustomerAsync(AsyncServiceScope scope, string customerCode)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        return await dbContext.Customers.AsNoTracking().SingleAsync(c => c.CustomerCode == customerCode);
    }

    /// <summary>
    /// <see cref="SalesService.CreateAsync"/> を呼び、永続化された行を検証コールバックへ渡した後、
    /// 作成した行を物理削除する。戻り値は採番された売上番号。
    /// </summary>
    private static async Task<string> CreateAndCleanupAsync(
        AsyncServiceScope scope,
        SalesEntity[] lines,
        RoundingType roundingType,
        Func<IReadOnlyList<SalesEntity>, Task> assert)
    {
        var salesService = scope.ServiceProvider.GetRequiredService<SalesService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();

        var slipNumber = await salesService.CreateAsync(lines, roundingType);

        try
        {
            var persisted = await dbContext.Sales
                .AsNoTracking()
                .Where(s => s.SalesSlipNumber == slipNumber)
                .OrderBy(s => s.LineNumber)
                .ToListAsync();

            Assert.NotEmpty(persisted);
            await assert(persisted);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.sales WHERE sales_slip_number = {slipNumber}");
        }

        return slipNumber;
    }

    private static SalesEntity NewLine(
        Customer customer,
        short lineNumber,
        TaxCategory taxCategory,
        decimal taxRate,
        decimal quantity,
        decimal unitPrice,
        string? lineRemarks = null,
        string? slipRemarks = null) => new()
    {
        SalesSlipNumber = string.Empty, // SalesService.CreateAsync が上書きする
        LineNumber = lineNumber, // SalesService.CreateAsync は行番号を振り直さないため、呼び出し元が正しい値を渡す
        SlipDate = DateOnly.FromDateTime(DateTime.Today),
        CustomerCode = customer.CustomerCode,
        TaxUnit = customer.TaxUnit,
        CustomerName = customer.CustomerName,
        SlipType = SlipType.Sales,
        ProductCode = "PRD001",
        ProductName = "テスト用商品",
        Quantity = quantity,
        UnitPrice = unitPrice,
        Amount = ConsumptionTaxCalculator.CalculateLineAmount(quantity, unitPrice, customer.RoundingType),
        CostPrice = 0m,
        TaxCategory = taxCategory,
        TaxRate = taxRate,
        DeliveryNoteIssueCount = 0,
        BillingStatus = BillingLinkStatus.Unbilled,
        SettlementStatus = SettlementStatus.Unsettled,
        SettledAmount = 0m,
        SlipRemarks = slipRemarks,
        LineRemarks = lineRemarks,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };
}
