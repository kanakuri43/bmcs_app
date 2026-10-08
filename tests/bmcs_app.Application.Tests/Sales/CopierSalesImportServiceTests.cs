using bmcs_app.Application.Sales;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Domain.Import;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Sales;

/// <summary>
/// コピー機売上CSV取込サービスの結合テスト（開発用ライブDB）。
/// 取込は <c>SalesService.CreateAsync</c> 自身のトランザクションを使うため外側トランザクション＋Rollback方式は使えず、
/// テスト用の得意先・機番・売上・履歴・（無ければ）汎用商品COPYCHGを作り、最後に必ず物理削除する。
/// 担当者は <see cref="DevDatabaseFixture"/> が保証する EMP001 を起動引数で渡して検証する。
/// </summary>
public class CopierSalesImportServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string Invoice = "__TCI01";   // 外税・請求単位（端数 切り捨て）
    private const string Slip = "__TCI02";      // 外税・伝票単位（端数 四捨五入）
    private const string Line = "__TCI03";      // 内税・明細単位
    private const string Closed = "__TCI04";    // 請求締め済み（確定請求 2026-09-30）
    private static readonly DateOnly Date = new(2026, 9, 20);

    [Fact]
    public Task 請求単位と伝票単位の得意先で税額_摘要_担当者_日付が期待どおり登録される() => RunAsync(async (sp, db) =>
    {
        var service = sp.GetRequiredService<CopierSalesImportService>();
        var rows = await service.PreviewAsync([Row("__TCIM1", 12345m), Row("__TCIM2", 12345m)]);
        Assert.All(rows, r => Assert.Equal(CopierImportStatus.Importable, r.Status));

        var results = await service.ImportAsync(rows);
        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));

        var invoice = await SingleLineAsync(db, results[0].SalesSlipNumber!);
        Assert.Equal(Invoice, invoice.CustomerCode);
        Assert.Equal("テスト請求単位", invoice.CustomerName);
        Assert.Equal(TaxUnit.Invoice, invoice.TaxUnit);
        Assert.Equal(Date, invoice.SlipDate);
        Assert.Equal("EMP001", invoice.EmployeeCode);
        Assert.Equal("COPYCHG", invoice.ProductCode);
        Assert.Equal(SlipType.Sales, invoice.SlipType);
        Assert.Equal((short)1, invoice.LineNumber);
        Assert.Equal(1m, invoice.Quantity);
        Assert.Equal(12345m, invoice.UnitPrice);
        Assert.Equal(12345m, invoice.Amount);
        Assert.Equal(10m, invoice.TaxRate);
        Assert.Null(invoice.SlipTaxAmount); // 請求単位は請求確定時に税額を決める
        Assert.Equal("機種X モノ1 フル2 フルP3", invoice.SlipRemarks);
        Assert.Equal("__TCIM1", invoice.InternalRemarks);
        Assert.Equal(0, invoice.DeliveryNoteIssueCount);

        // 伝票単位: 12345 × 10% = 1234.5 → 四捨五入で 1235。
        var slip = await SingleLineAsync(db, results[1].SalesSlipNumber!);
        Assert.Equal(12345m, slip.Amount);
        Assert.Equal(1235m, slip.SlipTaxAmount);

        var histories = await db.CopierImportHistories.AsNoTracking().Where(h => h.MachineNo.StartsWith("__TCIM")).ToListAsync();
        Assert.Equal(2, histories.Count);
        Assert.Equal(results[0].SalesSlipNumber, histories.Single(h => h.MachineNo == "__TCIM1").SalesSlipNumber);
    });

    [Fact]
    public Task 同一機番_締日の再取込は取込済みになり_取消済み売上なら再取込できる() => RunAsync(async (sp, db) =>
    {
        var service = sp.GetRequiredService<CopierSalesImportService>();
        var first = (await service.ImportAsync(await service.PreviewAsync([Row("__TCIM1", 1000m)])))[0];
        Assert.True(first.IsSuccess, first.Error);

        var again = await service.PreviewAsync([Row("__TCIM1", 1000m)]);
        Assert.Equal(CopierImportStatus.Imported, again[0].Status);
        Assert.Empty(await service.ImportAsync(again)); // 取込可でない行は処理されない

        await sp.GetRequiredService<SalesService>().CancelSlipAsync(first.SalesSlipNumber!);

        var reimport = await service.PreviewAsync([Row("__TCIM1", 1000m)]);
        Assert.Equal(CopierImportStatus.Importable, reimport[0].Status);
        var second = (await service.ImportAsync(reimport))[0];
        Assert.True(second.IsSuccess, second.Error);
        Assert.NotEqual(first.SalesSlipNumber, second.SalesSlipNumber);

        var history = await db.CopierImportHistories.AsNoTracking().SingleAsync(h => h.MachineNo == "__TCIM1");
        Assert.Equal(second.SalesSlipNumber, history.SalesSlipNumber);
    });

    [Fact]
    public Task 各種エラー判定があり_エラー行があっても他行は登録される() => RunAsync(async (sp, db) =>
    {
        var service = sp.GetRequiredService<CopierSalesImportService>();
        var rows = await service.PreviewAsync(
        [
            Row("__TCINONE", 1000m),                       // 未登録機番
            Row("__TCIM3", 1000m),                         // 内税明細単位
            Row("__TCIM4", 1000m),                         // 請求締め済み期間（2026-09-30確定）
            Row("__TCIM1", 0m),                            // 0円
            Row("__TCIM1", 5000m),                         // 正常
        ]);

        Assert.Equal(
            [CopierImportStatus.Error, CopierImportStatus.Error, CopierImportStatus.Error, CopierImportStatus.Excluded, CopierImportStatus.Importable],
            rows.Select(r => r.Status));
        Assert.Contains("登録されていない", rows[0].Reason);
        Assert.Contains("内税", rows[1].Reason);
        Assert.Contains("請求締め済み", rows[2].Reason);
        Assert.Equal(Closed, rows[2].CustomerCode);

        var results = await service.ImportAsync(rows);
        var ok = Assert.Single(results);
        Assert.True(ok.IsSuccess, ok.Error);
        Assert.Equal(5000m, (await SingleLineAsync(db, ok.SalesSlipNumber!)).Amount);
    });

    [Fact]
    public Task 合算行は合計金額で1伝票として登録される() => RunAsync(async (sp, db) =>
    {
        const string csv = "機番,機種名（漢字）,ユーザ請求CV（モノ）,ユーザ請求CV（フル）,ユーザ請求CV（フルＰ）,ユーザー請求金額（機器合計）-税別,締日\n"
            + "__TCIM2,機種X,1,2,3,3000,20260920\n"
            + "__TCIM2,機種X,0,0,0,4500,20260920\n";
        var parsed = CopierCsvParser.Parse(csv);
        var row = Assert.Single(parsed.Lines).Row!;
        Assert.Equal(2, row.MergedLineCount);

        var service = sp.GetRequiredService<CopierSalesImportService>();
        var preview = await service.PreviewAsync([row]);
        Assert.Equal(2, preview[0].MergedLineCount);
        var result = (await service.ImportAsync(preview))[0];
        Assert.True(result.IsSuccess, result.Error);

        // 伝票単位: 7500 × 10% = 750。
        var line = await SingleLineAsync(db, result.SalesSlipNumber!);
        Assert.Equal(7500m, line.Amount);
        Assert.Equal(750m, line.SlipTaxAmount);
    });

    // ── 準備と後始末 ──────────────────────────────

    private static CopierCsvRow Row(string machineNo, decimal amount) => new(
        LineNumber: 2, MachineNo: machineNo, ModelName: "機種X", AmountExcludingTax: amount, ClosingDate: Date,
        SlipRemarks: "機種X モノ1 フル2 フルP3", InternalRemarks: machineNo);

    private static async Task<SalesEntity> SingleLineAsync(BmcsDbContext db, string slipNumber)
        => await db.Sales.AsNoTracking().SingleAsync(s => s.SalesSlipNumber == slipNumber);

    private async Task RunAsync(Func<IServiceProvider, BmcsDbContext, Task> body)
    {
        _ = fixture.Services; // フィクスチャの初期化（EMP001 等の参照マスタ作成）を保証する
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication(configuration, ["EMP001"]);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();

        var createdProduct = false;
        try
        {
            await CleanupAsync(db, false);
            createdProduct = await SetupAsync(db);
            await body(scope.ServiceProvider, db);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await CleanupAsync(db, createdProduct);
        }
    }

    private static async Task<bool> SetupAsync(BmcsDbContext db)
    {
        var now = DateTime.Now;
        var createdProduct = false;
        if (!await db.Products.AnyAsync(p => p.ProductCode == CopierSalesImportService.ProductCode))
        {
            db.Products.Add(new Product
            {
                ProductCode = CopierSalesImportService.ProductCode, ProductName = "コピー機利用料",
                StandardUnitPriceExclTax = 0, StandardUnitPriceInclTax = 0, StandardCostPrice = 0,
                TaxCategory = TaxCategory.Standard,
                CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
            });
            createdProduct = true;
        }

        (string Code, string Name, byte Closing, TaxUnit Unit, RoundingType Rounding)[] customers =
        [
            (Invoice, "テスト請求単位", 15, TaxUnit.Invoice, RoundingType.Floor),
            (Slip, "テスト伝票単位", 99, TaxUnit.Slip, RoundingType.RoundHalfUp),
            (Line, "テスト内税", 0, TaxUnit.Line, RoundingType.Floor),
            (Closed, "テスト締め済み", 15, TaxUnit.Invoice, RoundingType.Floor),
        ];
        foreach (var (code, name, closing, unit, rounding) in customers)
        {
            db.Customers.Add(new Customer
            {
                CustomerCode = code, CustomerName = name, ClosingDay = closing, TaxUnit = unit, RoundingType = rounding,
                PrintRepresentativeFlag = false, BillingCustomerCode = code,
                CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
            });
        }
        await db.SaveChangesAsync();

        foreach (var (machine, customer) in new[] { ("__TCIM1", Invoice), ("__TCIM2", Slip), ("__TCIM3", Line), ("__TCIM4", Closed) })
        {
            db.CopierMachines.Add(new CopierMachine
            {
                MachineNo = machine, CustomerCode = customer,
                CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
            });
        }

        db.Billings.Add(new bmcs_app.Domain.Entities.Billing
        {
            BillingNumber = "__TCIBIL", CustomerCode = Closed, TaxUnit = TaxUnit.Invoice, CustomerName = "テスト締め済み",
            BillingDate = new DateOnly(2026, 9, 30), ClosingYearMonth = "202609",
            PreviousBalance = 0m, ReceiptAmount = 0m, SalesAmount = 0m, TaxAmount = 0m, CurrentBillingAmount = 0m,
            StandardRateTaxableAmount = 0m, StandardRateTaxAmount = 0m, ReducedRateTaxableAmount = 0m,
            ReducedRateTaxAmount = 0m, TaxExemptAmount = 0m,
            BillingStatus = BillingStatus.Confirmed, ConfirmedAt = now, ConfirmedBy = "TEST",
            CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return createdProduct;
    }

    /// <summary>テストで作ったデータだけを子→親の順に物理削除する（前回クラッシュの残骸も対象）。</summary>
    private static async Task CleanupAsync(BmcsDbContext db, bool deleteProduct)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.copier_import_histories WHERE machine_no LIKE '[_][_]TCIM%'");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.sales WHERE customer_code LIKE '[_][_]TCI0%'");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.copier_machines WHERE machine_no LIKE '[_][_]TCIM%'");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.billings WHERE billing_number = '__TCIBIL'");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM dbo.customers WHERE customer_code LIKE '[_][_]TCI0%'");
        if (deleteProduct)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.products WHERE product_code = {CopierSalesImportService.ProductCode}");
        }
    }
}
