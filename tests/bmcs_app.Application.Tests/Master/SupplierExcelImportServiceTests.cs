using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 仕入先マスタExcel取込のテスト。解析は DB を使わない。
/// 取込は専用コード（__XS*）で登録して finally で物理削除する（CopierExcelImportServiceTests と同じ方式）。
/// </summary>
public class SupplierExcelImportServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private static readonly string[] Header =
        ["仕入先コード", "仕入先名", "カナ", "郵便番号", "住所1", "住所2", "電話番号", "FAX番号", "先方担当者", "備考", "チェック結果"];

    /// <summary>テンプレートの1〜4行目（見出し・必須/任意・型・入力例）。データは5行目から。</summary>
    private static object?[][] WithTemplateTop(params object?[][] data) =>
    [
        [.. Header],
        ["必須", "必須"],
        ["型", "型"],
        ["S001", "入力例", null, "123-4567"],
        .. data,
    ];

    [Fact]
    public void 説明行と入力例を読み飛ばし_5行目から読み_不正な行はエラーにする()
    {
        var result = Parse(WithTemplateTop(
            ["A1", "正常", "カナ", "123-4567", "住所1", "住所2", "03-1", "03-2", "山田", "備考"],
            [null, null, null],
            ["", "コード空"],
            ["全角コード", "全角"],
            ["B1", "重複1"],
            ["B1", "重複2"],
            ["B2", ""],
            ["B3", new string('あ', 61)],
            ["B4", "郵便番号超過", null, "123456789"],
            ["B5", "担当者超過", null, null, null, null, null, null, new string('あ', 41)],
            ["B6", "最小"]));

        Assert.Null(result.FileError);
        Assert.Equal(5, result.Rows[0].RowNumber);
        Assert.Equal(
            [SupplierImportStatus.New, SupplierImportStatus.Error, SupplierImportStatus.Error, SupplierImportStatus.New,
             .. Enumerable.Repeat(SupplierImportStatus.Error, 5), SupplierImportStatus.New],
            result.Rows.Select(r => r.Status));
        Assert.Equal("山田", result.Rows[0].ContactPersonName);
        Assert.Contains("重複", result.Rows[4].Reason);
        Assert.Null(result.Rows[9].Remarks);

        Assert.NotNull(Parse([["仕入先コード"]]).FileError);
    }

    [Fact]
    public async Task 新規登録と上書き更新を行い_無効化済みは有効に戻す()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<SupplierExcelImportService>();
        string[] codes = ["__XS_NEW", "__XS_OLD", "__XS_DEL", "__XS_BAD"];

        var now = DateTime.Now;
        dbContext.Suppliers.AddRange(
            new Supplier { SupplierCode = "__XS_OLD", SupplierName = "旧名", Remarks = "旧備考", PhoneNumber = "000", CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now },
            new Supplier { SupplierCode = "__XS_DEL", SupplierName = "無効", IsDeleted = true, CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var path = WriteTempXlsx(WithTemplateTop(
            ["__XS_NEW", "新規", "カナ", "123-4567", "住所1", "住所2", "03-1", "03-2", "山田", "備考"],
            ["__XS_OLD", "新名"],
            ["__XS_DEL", "復活", null, null, null, null, "03-9"],
            ["__XS_BAD", ""]));
        try
        {
            var read = await service.ReadAsync(path);
            Assert.Equal(
                [SupplierImportStatus.New, SupplierImportStatus.Update, SupplierImportStatus.Update, SupplierImportStatus.Error],
                read.Rows.Select(r => r.Status));

            Assert.Equal((1, 2), await service.ImportAsync(read.Rows));
            dbContext.ChangeTracker.Clear();

            var saved = await dbContext.Suppliers.AsNoTracking().Where(s => codes.Contains(s.SupplierCode)).ToDictionaryAsync(s => s.SupplierCode);
            Assert.Equal(3, saved.Count);
            Assert.Equal(("新規", "山田", "備考", false), (saved["__XS_NEW"].SupplierName, saved["__XS_NEW"].ContactPersonName, saved["__XS_NEW"].Remarks, saved["__XS_NEW"].IsDeleted));
            // 上書きでは、Excelで空欄の項目は空になる。
            Assert.Equal(("新名", null, null, false), (saved["__XS_OLD"].SupplierName, saved["__XS_OLD"].PhoneNumber, saved["__XS_OLD"].Remarks, saved["__XS_OLD"].IsDeleted));
            Assert.Equal(("復活", "03-9", false), (saved["__XS_DEL"].SupplierName, saved["__XS_DEL"].PhoneNumber, saved["__XS_DEL"].IsDeleted));
        }
        finally
        {
            File.Delete(path);
            foreach (var code in codes)
            {
                await dbContext.Database.ExecuteSqlAsync($"DELETE FROM dbo.suppliers WHERE supplier_code = {code}");
            }
        }
    }

    private static SupplierImportReadResult Parse(object?[][] rows)
    {
        var path = WriteTempXlsx(rows);
        try
        {
            using var stream = File.OpenRead(path);
            return SupplierExcelImportService.Parse(stream);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempXlsx(object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("仕入先入力");
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                sheet.Cell(r + 1, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
            }
        }

        var path = Path.Combine(Path.GetTempPath(), $"supplier_import_{Guid.NewGuid():N}.xlsx");
        workbook.SaveAs(path);
        return path;
    }
}
