using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 単価・掛率マスタExcel取込のテスト。解析は DB を使わない。
/// 取込は専用の得意先（__XP*）に対して行い、finally で物理削除する。商品は DevDatabaseFixture が保証する PRD001／PRD002 を使う
/// （単価・掛率の行はテスト用得意先にだけ作るため、既存データと衝突しない）。
/// </summary>
public class CustomerProductPriceExcelImportServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    /// <summary>テンプレートの1〜4行目（見出し・必須/任意・型・入力例）。データは5行目から。</summary>
    private static object?[][] WithTemplateTop(params object?[][] data) =>
    [
        ["得意先コード", "商品コード", "単価", "掛率", "チェック結果"],
        ["必須", "必須", "どちらか", "どちらか", "自動"],
        ["得意先コード", "商品コード", "0以上", "0〜100", "OK以外は修正"],
        ["1001", "A-001", "1500", "80"],
        .. data,
    ];

    [Fact]
    public void 説明行と入力例を読み飛ばし_5行目から読み_不正な行はエラーにする()
    {
        var result = Parse(WithTemplateTop(
            ["C1", "P1", "1500.5", null],
            ["C1", "P2", null, "80"],
            [null, null, null, null],
            ["", "P3", "1"],
            ["C1", "", "1"],
            ["C1", "P4"],
            ["C1", "P5", "-1"],
            ["C1", "P6", "1.23456"],
            ["C1", "P7", "abc"],
            ["C1", "P8", null, "101"],
            ["C1", "P9", null, "80.5"],
            ["C1", "P1", "1"],
            ["C1", "P10", "0", "0"]));

        Assert.Null(result.FileError);
        Assert.Equal(5, result.Rows[0].RowNumber);
        Assert.Equal(
            [PriceImportStatus.New, PriceImportStatus.New, .. Enumerable.Repeat(PriceImportStatus.Error, 9), PriceImportStatus.New],
            result.Rows.Select(r => r.Status));
        Assert.Equal((1500.5m, (int?)null), (result.Rows[0].UnitPrice, result.Rows[0].Rate));
        Assert.Equal(((decimal?)null, (int?)80), (result.Rows[1].UnitPrice, result.Rows[1].Rate));
        Assert.Contains("両方空", result.Rows[4].Reason);
        Assert.Contains("重複", result.Rows[10].Reason);
        Assert.Equal((0m, (int?)0), (result.Rows[11].UnitPrice, result.Rows[11].Rate)); // 0 は有効な値

        Assert.NotNull(Parse([["得意先コード", "商品コード", "単価"]]).FileError);
    }

    [Fact]
    public async Task 追加と上書きと復活を行い_空欄は変更せず_無効な得意先と商品はエラーにする()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<CustomerProductPriceExcelImportService>();
        string[] customers = ["__XP_C1", "__XP_OFF"];

        await AddCustomerAsync(dbContext, "__XP_C1", isDeleted: false);
        await AddCustomerAsync(dbContext, "__XP_OFF", isDeleted: true);
        var now = DateTime.Now;
        // PRD001: 単価・掛率とも有効 / PRD002: 単価は無効化済み・掛率なし
        dbContext.CustomerProductPrices.AddRange(
            new CustomerProductPrice { CustomerCode = "__XP_C1", ProductCode = "PRD001", UnitPrice = 100m, CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now },
            new CustomerProductPrice { CustomerCode = "__XP_C1", ProductCode = "PRD002", UnitPrice = 200m, IsDeleted = true, CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now });
        dbContext.CustomerProductRates.Add(
            new CustomerProductRate { CustomerCode = "__XP_C1", ProductCode = "PRD001", CatalogPriceRate = 70, CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var path = WriteTempXlsx(WithTemplateTop(
            ["__XP_C1", "PRD001", null, "85"],       // 掛率だけ上書き（単価は変更しない）→ 更新
            ["__XP_C1", "PRD002", "250.5", "90"],    // 無効化済みの単価を復活・掛率は追加 → 新規
            ["__XP_NONE", "PRD001", "1"],            // 得意先なし
            ["__XP_OFF", "PRD001", "1"],             // 得意先が無効
            ["__XP_C1", "__XP_NOPRD", "1"]));        // 商品なし
        try
        {
            var read = await service.ReadAsync(path);
            Assert.Equal(
                [PriceImportStatus.Update, PriceImportStatus.New, PriceImportStatus.Error, PriceImportStatus.Error, PriceImportStatus.Error],
                read.Rows.Select(r => r.Status));
            Assert.Equal("テスト用得意先", read.Rows[0].CustomerName);
            Assert.Contains("得意先コード", read.Rows[2].Reason);
            Assert.Contains("得意先コード", read.Rows[3].Reason);
            Assert.Contains("商品コード", read.Rows[4].Reason);

            Assert.Equal((1, 1), await service.ImportAsync(read.Rows));
            dbContext.ChangeTracker.Clear();

            var prices = await dbContext.CustomerProductPrices.AsNoTracking().Where(p => p.CustomerCode == "__XP_C1").ToDictionaryAsync(p => p.ProductCode);
            var rates = await dbContext.CustomerProductRates.AsNoTracking().Where(p => p.CustomerCode == "__XP_C1").ToDictionaryAsync(p => p.ProductCode);
            Assert.Equal((100m, false), (prices["PRD001"].UnitPrice, prices["PRD001"].IsDeleted)); // 空欄は変更しない
            Assert.Equal((85, false), (rates["PRD001"].CatalogPriceRate, rates["PRD001"].IsDeleted));
            Assert.Equal((250.5m, false), (prices["PRD002"].UnitPrice, prices["PRD002"].IsDeleted)); // 復活して新しい値
            Assert.Equal((90, false), (rates["PRD002"].CatalogPriceRate, rates["PRD002"].IsDeleted));
            Assert.Equal(2, prices.Count);
            Assert.Equal(2, rates.Count);

            // 取込後に商品が無効化されたら、実行直前の見直しで何も反映せず例外にする。
            await dbContext.Database.ExecuteSqlAsync($"UPDATE dbo.customers SET is_deleted = 1 WHERE customer_code = {"__XP_C1"}");
            await Assert.ThrowsAsync<CustomerProductPriceValidationException>(() => service.ImportAsync(read.Rows));
        }
        finally
        {
            File.Delete(path);
            foreach (var code in customers)
            {
                await dbContext.Database.ExecuteSqlAsync($"DELETE FROM dbo.customer_product_prices WHERE customer_code = {code}");
                await dbContext.Database.ExecuteSqlAsync($"DELETE FROM dbo.customer_product_rates WHERE customer_code = {code}");
                await dbContext.Database.ExecuteSqlAsync($"DELETE FROM dbo.customers WHERE customer_code = {code}");
            }
        }
    }

    private static async Task AddCustomerAsync(BmcsDbContext dbContext, string code, bool isDeleted)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new Customer
        {
            CustomerCode = code, CustomerName = "テスト用得意先", ClosingDay = 20, TaxUnit = TaxUnit.Invoice,
            RoundingType = RoundingType.Floor, PrintRepresentativeFlag = false, BillingCustomerCode = code,
            IsDeleted = isDeleted, CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
        });
        await dbContext.SaveChangesAsync();
    }

    private static PriceImportReadResult Parse(object?[][] rows)
    {
        var path = WriteTempXlsx(rows);
        try
        {
            using var stream = File.OpenRead(path);
            return CustomerProductPriceExcelImportService.Parse(stream);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempXlsx(object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("単価掛率入力");
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                sheet.Cell(r + 1, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
            }
        }

        var path = Path.Combine(Path.GetTempPath(), $"price_import_{Guid.NewGuid():N}.xlsx");
        workbook.SaveAs(path);
        return path;
    }
}
