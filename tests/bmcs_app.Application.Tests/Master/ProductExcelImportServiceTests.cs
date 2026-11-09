using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 商品マスタExcel取込のテスト。解析は DB を使わず、取込は外側をトランザクションで包んで最後に必ず Rollback する。
/// </summary>
public class ProductExcelImportServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private static readonly object?[] Header = ["メーカー名", "ブランド名", "商品コード", "商品名", "品番", "JANコード", "表示定価", "最新価格相当"];

    [Fact]
    public void 見出しの揃ったシートを読み_名称を連結して数値セルを文字列にする()
    {
        var result = Parse(
            ("design", [["設計メモ"]]),
            ("sample", [Header,
                ["メーカー", "ブランド　　", 13020404, "ﾊﾞｯｸﾙｺﾝﾃﾅ", "BL-13", 4905009406210, 0, 467.5],
                ["廃止メーカー", "NULL", 81017, "替ｺﾞﾑ", 500, null, 600, 360],
                [null, null, null, null, null, null, null, null]]));

        Assert.Null(result.FileError);
        Assert.Equal(2, result.Rows.Count);
        var first = result.Rows[0];
        Assert.Equal((2, "13020404", "ブランド ﾊﾞｯｸﾙｺﾝﾃﾅ BL-13", "4905009406210", 467.5m, 0m, ProductImportStatus.New),
            (first.RowNumber, first.ProductCode, first.ProductName, first.JanCode, first.CostPrice, first.CatalogPrice, first.Status));
        var second = result.Rows[1];
        Assert.Equal(("替ｺﾞﾑ 500", (string?)null, 360m, 600m), (second.ProductName, second.JanCode, second.CostPrice, second.CatalogPrice));
    }

    [Fact]
    public void 不正な行はエラーにし_見出しが無ければファイルエラーにする()
    {
        var result = Parse(("sample", [Header,
            ["m", "b", null, "名前", "p", null, 0, 1],
            ["m", "b", "A", "名前", "p", null, 0, 1],
            ["m", "b", "A", "名前", "p", null, 0, 1],
            ["m", "b", "B", "名前", "p", "12345678901234", 0, 1],
            ["m", "b", "C", "名前", "p", null, 0, -1],
            ["m", "b", "D", "名前", "p", null, "abc", 1],
            ["m", "", "E", new string('あ', 201), "", null, 0, 1]]));

        Assert.Equal(
            [ProductImportStatus.Error, ProductImportStatus.New, ProductImportStatus.Error, ProductImportStatus.Error,
             ProductImportStatus.Error, ProductImportStatus.Error, ProductImportStatus.Error],
            result.Rows.Select(r => r.Status));
        Assert.Contains("重複", result.Rows[2].Reason);

        Assert.NotNull(Parse(("sample", [["商品コード", "商品名"]])).FileError);
    }

    [Fact]
    public Task 新規は登録し_既存は名称JAN原価定価だけを上書きする() => RunAsync(async sp =>
    {
        var dbContext = sp.GetRequiredService<BmcsDbContext>();
        dbContext.Products.Add(new Product
        {
            ProductCode = "__XL01", ProductName = "旧名称", ProductNameKana = "ｶﾅ", UnitName = "個",
            StandardUnitPriceExclTax = 100, StandardUnitPriceInclTax = 110, StandardCostPrice = 50,
            TaxCategory = TaxCategory.Reduced,
            CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var path = WriteTempXlsx(("sample", [Header,
            ["m", "b", "__XL01", "新名称", "p1", 4900000000001, 1000, 600],
            ["m", "b", "__XL02", "新商品", "p2", null, 0, 99.5]]));
        try
        {
            var service = sp.GetRequiredService<ProductExcelImportService>();
            var read = await service.ReadAsync(path);
            Assert.Equal([ProductImportStatus.Update, ProductImportStatus.New], read.Rows.Select(r => r.Status));

            Assert.Equal((1, 1), await service.ImportAsync(read.Rows));
            dbContext.ChangeTracker.Clear();

            var updated = await dbContext.Products.AsNoTracking().SingleAsync(p => p.ProductCode == "__XL01");
            Assert.Equal(("b 新名称 p1", "4900000000001", 600m, 1000m), (updated.ProductName, updated.JanCode, updated.StandardCostPrice, updated.CatalogPrice));
            Assert.Equal(("ｶﾅ", "個", 100m, TaxCategory.Reduced), (updated.ProductNameKana, updated.UnitName, updated.StandardUnitPriceExclTax, updated.TaxCategory));

            var inserted = await dbContext.Products.AsNoTracking().SingleAsync(p => p.ProductCode == "__XL02");
            Assert.Equal(("b 新商品 p2", (string?)null, 99.5m, 0m, TaxCategory.Standard, 0m, false),
                (inserted.ProductName, inserted.JanCode, inserted.StandardCostPrice, inserted.CatalogPrice, inserted.TaxCategory, inserted.StandardUnitPriceExclTax, inserted.IsDeleted));
        }
        finally
        {
            File.Delete(path);
        }
    });

    private static ProductImportReadResult Parse(params (string Name, object?[][] Rows)[] sheets)
    {
        var path = WriteTempXlsx(sheets);
        try
        {
            using var stream = File.OpenRead(path);
            return ProductExcelImportService.Parse(stream);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempXlsx(params (string Name, object?[][] Rows)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, rows) in sheets)
        {
            var sheet = workbook.AddWorksheet(name);
            for (var r = 0; r < rows.Length; r++)
            {
                for (var c = 0; c < rows[r].Length; c++)
                {
                    sheet.Cell(r + 1, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
                }
            }
        }

        var path = Path.Combine(Path.GetTempPath(), $"product_import_{Guid.NewGuid():N}.xlsx");
        workbook.SaveAs(path);
        return path;
    }

    private async Task RunAsync(Func<IServiceProvider, Task> body)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await body(scope.ServiceProvider);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
