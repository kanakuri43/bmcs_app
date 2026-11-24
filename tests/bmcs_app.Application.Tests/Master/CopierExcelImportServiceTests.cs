using bmcs_app.Application.Master;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// コピー機マスタExcel取込のテスト。解析は DB を使わない。
/// 取込は専用コード（__XM*）で登録して finally で物理削除する（CustomerExcelImportServiceTests と同じ方式）。
/// </summary>
public class CopierExcelImportServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    /// <summary>テンプレートの1〜4行目（見出し・必須/任意・型・入力例）。データは5行目から。</summary>
    private static object?[][] WithTemplateTop(params object?[][] data) =>
    [
        ["機番", "得意先コード", "備考", "チェック結果"],
        ["必須", "必須", "任意", "自動"],
        ["半角英数 20文字以内", "得意先コード", "100文字以内", "OK以外は修正"],
        ["AB123456", "1001", "本社3階"],
        .. data,
    ];

    [Fact]
    public void 説明行と入力例を読み飛ばし_5行目から読み_不正な行はエラーにする()
    {
        var result = Parse(WithTemplateTop(
            ["M1", "C1", "営業所"],
            [null, null, null],
            ["", "C1"],
            ["全角機番", "C1"],
            ["M1", "C1"],
            ["M2", ""],
            ["M3", "12345678901"],
            ["M4", "C1", new string('あ', 101)],
            ["M5", "C1"]));

        Assert.Null(result.FileError);
        Assert.Equal(5, result.Rows[0].RowNumber);
        Assert.Equal(
            [CopierImportStatus.New, CopierImportStatus.Error, CopierImportStatus.Error, CopierImportStatus.Error,
             CopierImportStatus.Error, CopierImportStatus.Error, CopierImportStatus.Error, CopierImportStatus.New],
            result.Rows.Select(r => r.Status));
        Assert.Contains("重複", result.Rows[3].Reason);
        Assert.Equal("営業所", result.Rows[0].Remarks);
        Assert.Null(result.Rows[7].Remarks);

        Assert.NotNull(Parse([["機番"]]).FileError);
    }

    [Fact]
    public async Task 新規登録と上書き更新を行い_得意先が無効ならエラーにする()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<CopierExcelImportService>();
        string[] machineNos = ["__XM_NEW", "__XM_OLD", "__XM_DEL", "__XM_NOCUS", "__XM_INACT"];
        string[] customers = ["__XM_C1", "__XM_C2", "__XM_OFF"];

        await AddCustomerAsync(dbContext, "__XM_C1", isDeleted: false);
        await AddCustomerAsync(dbContext, "__XM_C2", isDeleted: false);
        await AddCustomerAsync(dbContext, "__XM_OFF", isDeleted: true);
        var now = DateTime.Now;
        dbContext.CopierMachines.AddRange(
            new CopierMachine { MachineNo = "__XM_OLD", CustomerCode = "__XM_C1", Remarks = "旧", CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now },
            new CopierMachine { MachineNo = "__XM_DEL", CustomerCode = "__XM_C1", IsDeleted = true, CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var path = WriteTempXlsx(WithTemplateTop(
            ["__XM_NEW", "__XM_C1", "新規"],
            ["__XM_OLD", "__XM_C2", null],
            ["__XM_DEL", "__XM_C2", "復活"],
            ["__XM_NOCUS", "__XM_NONE"],
            ["__XM_INACT", "__XM_OFF"]));
        try
        {
            var read = await service.ReadAsync(path);
            Assert.Equal(
                [CopierImportStatus.New, CopierImportStatus.Update, CopierImportStatus.Update,
                 CopierImportStatus.Error, CopierImportStatus.Error],
                read.Rows.Select(r => r.Status));
            Assert.Equal("テスト用得意先", read.Rows[0].CustomerName);
            Assert.Contains("得意先コード", read.Rows[3].Reason);

            Assert.Equal((1, 2), await service.ImportAsync(read.Rows));
            dbContext.ChangeTracker.Clear();

            var saved = await dbContext.CopierMachines.AsNoTracking().Where(m => machineNos.Contains(m.MachineNo)).ToDictionaryAsync(m => m.MachineNo);
            Assert.Equal(3, saved.Count);
            Assert.Equal(("__XM_C1", "新規", false), (saved["__XM_NEW"].CustomerCode, saved["__XM_NEW"].Remarks, saved["__XM_NEW"].IsDeleted));
            Assert.Equal(("__XM_C2", null, false), (saved["__XM_OLD"].CustomerCode, saved["__XM_OLD"].Remarks, saved["__XM_OLD"].IsDeleted));
            Assert.Equal(("__XM_C2", "復活", false), (saved["__XM_DEL"].CustomerCode, saved["__XM_DEL"].Remarks, saved["__XM_DEL"].IsDeleted));

            // 取込後に得意先が無効化されたら、実行直前の見直しで何も登録せず例外にする。
            await dbContext.Database.ExecuteSqlAsync($"UPDATE dbo.customers SET is_deleted = 1 WHERE customer_code = {"__XM_C2"}");
            await Assert.ThrowsAsync<CopierMachineValidationException>(() => service.ImportAsync(read.Rows));
        }
        finally
        {
            File.Delete(path);
            foreach (var no in machineNos)
            {
                await dbContext.Database.ExecuteSqlAsync($"DELETE FROM dbo.copier_machines WHERE machine_no = {no}");
            }

            foreach (var code in customers)
            {
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

    private static CopierImportReadResult Parse(object?[][] rows)
    {
        var path = WriteTempXlsx(rows);
        try
        {
            using var stream = File.OpenRead(path);
            return CopierExcelImportService.Parse(stream);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempXlsx(object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("コピー機入力");
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                sheet.Cell(r + 1, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
            }
        }

        var path = Path.Combine(Path.GetTempPath(), $"copier_import_{Guid.NewGuid():N}.xlsx");
        workbook.SaveAs(path);
        return path;
    }
}
