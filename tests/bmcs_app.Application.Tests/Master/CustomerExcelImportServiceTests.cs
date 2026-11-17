using bmcs_app.Application.Master;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 得意先マスタExcel取込のテスト。解析は DB を使わない。
/// 取込は内部で独自に BeginTransactionAsync するため、外側トランザクションは使えず、
/// 専用コード（__XC*）で登録して finally で物理削除する（BillingClosingServiceTests と同じ方式）。
/// </summary>
public class CustomerExcelImportServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private static readonly string[] Header =
    [
        "得意先コード", "得意先名", "カナ", "郵便番号", "住所1", "住所2", "電話番号", "FAX番号", "得意先担当者名",
        "営業担当社員コード", "締日", "税区分", "端数区分", "代表者印字", "請求得意先コード", "振込先口座1", "振込先口座2",
    ];

    /// <summary>テンプレートの1〜4行目（見出し・必須/任意・型・入力例）。データは5行目から。</summary>
    private static object?[][] WithTemplateTop(params object?[][] data) =>
    [
        [.. Header],
        ["必須", "必須"],
        ["型", "型"],
        ["1001", "入力例", null, null, null, null, null, null, null, null, "20", "1", "2", "1"],
        .. data,
    ];

    private static object?[] Row(string code, string name, string closing, string tax, string rounding,
        string print = "0", string? billing = null, string? emp = null, string? bank1 = null, string? bank2 = null) =>
        [code, name, null, null, null, null, null, null, null, emp, closing, tax, rounding, print, billing, bank1, bank2];

    [Fact]
    public void 説明行と入力例を読み飛ばし_5行目から読む()
    {
        var result = Parse(WithTemplateTop(
            Row("A1", "締め会社", "99", "2", "3", "1"),
            Row("A2", "学校", "0", "3", "1"),
            new object?[17]));

        Assert.Null(result.FileError);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(5, result.Rows[0].RowNumber);
        var first = result.Rows[0];
        Assert.Equal((CustomerImportStatus.New, (byte?)99, TaxUnit.Slip, RoundingType.Ceiling, true, "A1"),
            (first.Status, first.ClosingDay, first.TaxUnit, first.RoundingType, first.PrintRepresentativeFlag, first.BillingCustomerCode));
    }

    [Fact]
    public void 不正な行はエラーにし_見出しが無ければファイルエラーにする()
    {
        var result = Parse(WithTemplateTop(
            Row("", "コード空", "20", "1", "1"),
            Row("全角コード", "全角", "20", "1", "1"),
            Row("B1", "重複1", "20", "1", "1"),
            Row("B1", "重複2", "20", "1", "1"),
            Row("B2", "締日32", "32", "1", "1"),
            Row("B3", "締日0で請求単位", "0", "1", "1"),
            Row("B4", "締日20で内税明細", "20", "3", "1"),
            Row("B5", "端数4", "20", "1", "4"),
            Row("B6", "印字2", "20", "1", "1", print: "2"),
            Row("B7", "口座同一", "20", "1", "1", bank1: "X", bank2: "X"),
            Row("B8", new string('あ', 61), "20", "1", "1"),
            Row("OK1", "正常", "20", "1", "1")));

        Assert.Equal(
            [CustomerImportStatus.Error, CustomerImportStatus.Error, CustomerImportStatus.New,
             .. Enumerable.Repeat(CustomerImportStatus.Error, 8), CustomerImportStatus.New],
            result.Rows.Select(r => r.Status));
        Assert.Contains("重複", result.Rows[3].Reason);

        Assert.NotNull(Parse([["得意先コード", "得意先名"]]).FileError);
    }

    [Fact]
    public async Task 新規登録し_請求集約元は後から更新して登録し_DBの検証でエラーにする()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<CustomerExcelImportService>();
        string[] codes = ["__XC_ROOT", "__XC_CHILD", "__XC_ONE", "__XC_BAD1", "__XC_BAD2", "__XC_BAD3"];

        var path = WriteTempXlsx(WithTemplateTop(
            // 集約元を集約先より先の行に置いても登録できる（2段階登録）
            Row("__XC_CHILD", "集約元", "20", "1", "2", billing: "__XC_ROOT"),
            Row("__XC_ROOT", "集約先", "20", "1", "2", print: "1", emp: "EMP001", bank1: "BNK001", bank2: "1"),
            Row("__XC_ONE", "都度", "0", "3", "1"),
            Row("__XC_BAD1", "社員なし", "20", "1", "1", emp: "__NOEMP"),
            Row("__XC_BAD2", "口座なし", "20", "1", "1", bank1: "__NOBANK"),
            Row("__XC_BAD3", "締日違いの集約", "15", "1", "2", billing: "__XC_ROOT")));
        try
        {
            var read = await service.ReadAsync(path);
            Assert.Equal(
                [CustomerImportStatus.New, CustomerImportStatus.New, CustomerImportStatus.New,
                 CustomerImportStatus.Error, CustomerImportStatus.Error, CustomerImportStatus.Error],
                read.Rows.Select(r => r.Status));
            Assert.Contains("社員マスタ", read.Rows[3].Reason);
            Assert.Contains("口座マスタ", read.Rows[4].Reason);
            Assert.Contains("締め日", read.Rows[5].Reason);

            Assert.Equal(3, await service.ImportAsync(read.Rows));
            dbContext.ChangeTracker.Clear();

            var saved = await dbContext.Customers.AsNoTracking().Where(c => codes.Contains(c.CustomerCode)).ToDictionaryAsync(c => c.CustomerCode);
            Assert.Equal(3, saved.Count);
            Assert.Equal("__XC_ROOT", saved["__XC_CHILD"].BillingCustomerCode);
            Assert.Equal("__XC_ROOT", saved["__XC_ROOT"].BillingCustomerCode);
            Assert.Equal(("EMP001", "BNK001", "1", true, RoundingType.RoundHalfUp),
                (saved["__XC_ROOT"].SalesEmployeeCode, saved["__XC_ROOT"].BankAccountCode1, saved["__XC_ROOT"].BankAccountCode2,
                 saved["__XC_ROOT"].PrintRepresentativeFlag, saved["__XC_ROOT"].RoundingType));
            Assert.Equal((byte)0, saved["__XC_ONE"].ClosingDay);

            // 登録後に同じファイルを読み直すと、全行「登録済み」でエラーになる（上書きしない）
            var again = await service.ReadAsync(path);
            Assert.All(again.Rows.Take(3), r => Assert.Contains("既に登録", r.Reason));
        }
        finally
        {
            File.Delete(path);
            // 集約元（子）を先に消す（自己参照FKのため）。
            foreach (var code in new[] { "__XC_CHILD" }.Concat(codes))
            {
                await dbContext.Database.ExecuteSqlAsync($"DELETE FROM dbo.customers WHERE customer_code = {code}");
            }
        }
    }

    [Fact]
    public async Task 集約先がエラーの行は集約元も取り込まない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<CustomerExcelImportService>();
        var path = WriteTempXlsx(WithTemplateTop(
            Row("__XC_P", "集約先(社員なし)", "20", "1", "2", emp: "__NOEMP"),
            Row("__XC_Q", "集約元", "20", "1", "2", billing: "__XC_P")));
        try
        {
            var read = await service.ReadAsync(path);
            Assert.All(read.Rows, r => Assert.Equal(CustomerImportStatus.Error, r.Status));
            Assert.Contains("エラーのため", read.Rows[1].Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CustomerImportReadResult Parse(object?[][] rows)
    {
        var path = WriteTempXlsx(rows);
        try
        {
            using var stream = File.OpenRead(path);
            return CustomerExcelImportService.Parse(stream);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempXlsx(object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("得意先入力");
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                // テンプレートは全セル文字列書式のため、数字も文字列で入る。
                sheet.Cell(r + 1, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
            }
        }

        var path = Path.Combine(Path.GetTempPath(), $"customer_import_{Guid.NewGuid():N}.xlsx");
        workbook.SaveAs(path);
        return path;
    }
}
