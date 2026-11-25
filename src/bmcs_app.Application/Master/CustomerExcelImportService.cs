using System.Globalization;
using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>プレビュー行の判定状態。</summary>
public enum CustomerImportStatus
{
    /// <summary>未登録の得意先コード。新規登録する。</summary>
    New,
    /// <summary>取込不可（<see cref="CustomerImportRow.Reason"/> に理由）。</summary>
    Error,
}

/// <summary>
/// Excelの1行。数値系の列は解析できなければ null（その行は <see cref="CustomerImportStatus.Error"/>）。
/// <see cref="BillingCustomerCode"/> は空欄なら自分自身のコード。
/// </summary>
public sealed record CustomerImportRow(
    int RowNumber,
    string CustomerCode,
    string CustomerName,
    string? CustomerNameKana,
    string? PostalCode,
    string? Address1,
    string? Address2,
    string? PhoneNumber,
    string? FaxNumber,
    string? ContactPersonName,
    string? SalesEmployeeCode,
    byte? ClosingDay,
    TaxUnit? TaxUnit,
    RoundingType? RoundingType,
    bool? PrintRepresentativeFlag,
    string BillingCustomerCode,
    string? BankAccountCode1,
    string? BankAccountCode2,
    CustomerImportStatus Status,
    string? Reason)
{
    /// <summary>登録用のエンティティ。エラー行では呼ばない（検証用には監査列に仮値を渡す）。</summary>
    internal Customer ToCustomer(string employeeCode, DateTime now) => new()
    {
        CustomerCode = CustomerCode,
        CustomerName = CustomerName,
        CustomerNameKana = CustomerNameKana,
        PostalCode = PostalCode,
        Address1 = Address1,
        Address2 = Address2,
        PhoneNumber = PhoneNumber,
        FaxNumber = FaxNumber,
        ContactPersonName = ContactPersonName,
        SalesEmployeeCode = SalesEmployeeCode,
        ClosingDay = ClosingDay!.Value,
        TaxUnit = TaxUnit!.Value,
        RoundingType = RoundingType!.Value,
        PrintRepresentativeFlag = PrintRepresentativeFlag!.Value,
        BillingCustomerCode = BillingCustomerCode,
        BankAccountCode1 = BankAccountCode1,
        BankAccountCode2 = BankAccountCode2,
        CreatedBy = employeeCode,
        CreatedAt = now,
        UpdatedBy = employeeCode,
        UpdatedAt = now,
    };
}

/// <summary>読込結果。<see cref="FileError"/> が非nullならファイル全体が取込不可。</summary>
public sealed record CustomerImportReadResult(string? FileError, IReadOnlyList<CustomerImportRow> Rows);

/// <summary>
/// 得意先マスタのExcel取込（docs/design_document.md 39章）。1行目の見出し名で列を特定し、
/// 新規の得意先だけを全件1トランザクションで登録する（既存コードは上書きしない）。
/// </summary>
public class CustomerExcelImportService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<CustomerExcelImportService> logger)
{
    /// <summary>テンプレートは2〜4行目が説明・入力例のため、データは5行目から読む。</summary>
    private const int FirstDataRow = 5;

    private static readonly string[] RequiredHeaders = ["得意先コード", "得意先名", "締日", "税区分", "端数区分", "代表者印字"];

    /// <summary>Excelを読み、行ごとに検証して新規／エラーを判定する。</summary>
    public async Task<CustomerImportReadResult> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        // Excelで開いたままでも読めるよう、共有読み取りで開く。
        var parsed = await Task.Run(() =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Parse(stream);
        }, cancellationToken);
        return parsed.FileError is not null
            ? parsed
            : new CustomerImportReadResult(null, await ValidateAgainstDbAsync(parsed.Rows, cancellationToken));
    }

    /// <summary>ブックを解析する（DBを見ない）。見出しが揃った最初のシートを対象にする。</summary>
    public static CustomerImportReadResult Parse(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        return ExcelImportReader.FindSheet(workbook, RequiredHeaders) is var (sheet, columns)
            ? new CustomerImportReadResult(null, ParseRows(sheet, columns))
            : new CustomerImportReadResult(ExcelImportReader.MissingHeadersMessage(RequiredHeaders), []);
    }

    private static List<CustomerImportRow> ParseRows(IXLWorksheet sheet, Dictionary<string, int> columns)
    {
        var rows = new List<CustomerImportRow>();
        var seen = new HashSet<string>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = FirstDataRow; r <= lastRow; r++)
        {
            // 見出しの無い任意列は空として扱う。
            string Text(string header) => ExcelImportReader.Text(sheet, columns, r, header);
            string? Opt(string header) => Text(header) is { Length: > 0 } t ? t : null;

            var code = Text("得意先コード");
            var name = Text("得意先名");
            var closing = Text("締日");
            var tax = Text("税区分");
            var rounding = Text("端数区分");
            var print = Text("代表者印字");
            var billing = Text("請求得意先コード");
            var bank1 = Opt("振込先口座1");
            var bank2 = Opt("振込先口座2");
            string?[] optionals = [Opt("カナ"), Opt("郵便番号"), Opt("住所1"), Opt("住所2"), Opt("電話番号"), Opt("FAX番号"),
                Opt("得意先担当者名"), Opt("営業担当社員コード"), bank1, bank2];
            if (new[] { code, name, closing, tax, rounding, print, billing }.All(s => s.Length == 0) && optionals.All(s => s is null))
            {
                continue; // 書式・数式だけ残った空行
            }

            var closingValue = ParseInt(closing, 0, 99);
            byte? closingDay = closingValue is <= 31 or 99 ? (byte?)closingValue : null;
            TaxUnit? taxUnit = (TaxUnit?)ParseInt(tax, 1, 3);
            RoundingType? roundingType = (RoundingType?)ParseInt(rounding, 1, 3);
            bool? printFlag = ParseInt(print, 0, 1) == 1 ? true : ParseInt(print, 0, 1) == 0 ? false : null;
            var sales = Opt("営業担当社員コード");

            var tooLong = TooLong(("得意先名", name, 60), ("カナ", Opt("カナ"), 60), ("郵便番号", Opt("郵便番号"), 8),
                    ("住所1", Opt("住所1"), 100), ("住所2", Opt("住所2"), 100), ("電話番号", Opt("電話番号"), 20),
                    ("FAX番号", Opt("FAX番号"), 20), ("得意先担当者名", Opt("得意先担当者名"), 40),
                    ("営業担当社員コード", sales, 10), ("請求得意先コード", billing, 10),
                    ("振込先口座1", bank1, 10), ("振込先口座2", bank2, 10));

            string? error =
                code.Length == 0 ? "得意先コードが空です。"
                : code.Length > 10 || !code.All(ch => ch is > ' ' and < '\u007f') ? "得意先コードは半角（英数記号）10文字以内にしてください。"
                : !seen.Add(code) ? "得意先コードがファイル内で重複しています。"
                : name.Length == 0 ? "得意先名が空です。"
                : tooLong is not null ? tooLong
                : closingDay is null ? "締日は 0／1〜31／99 のいずれかにしてください。"
                : taxUnit is null ? "税区分は 1／2／3 のいずれかにしてください。"
                : roundingType is null ? "端数区分は 1／2／3 のいずれかにしてください。"
                : printFlag is null ? "代表者印字は 0／1 のいずれかにしてください。"
                : !CustomerService.IsValidTaxUnitClosingDayCombo(closingDay.Value, taxUnit.Value)
                    ? "締日と税区分の組み合わせが不正です（締日0＝税区分3、締日1〜31・99＝税区分1か2）。"
                : bank1 is not null && bank1 == bank2 ? "振込先口座1と2に同じ口座は指定できません。"
                : null;

            rows.Add(new CustomerImportRow(
                r, code, name, Opt("カナ"), Opt("郵便番号"), Opt("住所1"), Opt("住所2"), Opt("電話番号"), Opt("FAX番号"),
                Opt("得意先担当者名"), sales, closingDay, taxUnit, roundingType, printFlag,
                billing.Length == 0 ? code : billing, bank1, bank2,
                error is null ? CustomerImportStatus.New : CustomerImportStatus.Error, error));
        }

        return rows;
    }

    private static string? TooLong(params (string Label, string? Value, int Max)[] fields)
        => fields.FirstOrDefault(f => f.Value?.Length > f.Max) is { Value: not null } over
            ? $"{over.Label}が{over.Max}文字を超えています。"
            : null;

    /// <summary>整数文字列を範囲内で解析する。範囲外・整数でなければ null。</summary>
    private static int? ParseInt(string text, int min, int max)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max ? v : null;

    /// <summary>
    /// DBとの突き合わせ（コードの登録済み・社員・口座・請求集約リンク）。解析でエラーでない行だけを見て、
    /// 新たにエラーになった行は理由つきで返す。プレビューと取込実行の両方から呼ぶ。
    /// </summary>
    private async Task<List<CustomerImportRow>> ValidateAgainstDbAsync(IReadOnlyList<CustomerImportRow> rows, CancellationToken cancellationToken)
    {
        var targets = rows.Where(r => r.Status != CustomerImportStatus.Error).ToList();
        var codes = targets.Select(r => r.CustomerCode).ToList();
        var employeeCodes = targets.Select(r => r.SalesEmployeeCode).OfType<string>().Distinct().ToList();
        var bankCodes = targets.SelectMany(r => new[] { r.BankAccountCode1, r.BankAccountCode2 }).OfType<string>().Distinct().ToList();
        var billingCodes = targets.Where(r => r.BillingCustomerCode != r.CustomerCode).Select(r => r.BillingCustomerCode).Distinct().ToList();

        var registered = (await dbContext.Customers.AsNoTracking()
            .Where(c => codes.Contains(c.CustomerCode)).Select(c => c.CustomerCode).ToListAsync(cancellationToken)).ToHashSet();
        var validEmployees = (await dbContext.Employees.AsNoTracking()
            .Where(e => employeeCodes.Contains(e.EmployeeCode) && !e.IsDeleted).Select(e => e.EmployeeCode).ToListAsync(cancellationToken)).ToHashSet();
        var validBanks = (await dbContext.BankAccounts.AsNoTracking()
            .Where(b => bankCodes.Contains(b.BankAccountCode) && !b.IsDeleted).Select(b => b.BankAccountCode).ToListAsync(cancellationToken)).ToHashSet();
        var dbBillingTargets = await dbContext.Customers.AsNoTracking()
            .Where(c => billingCodes.Contains(c.CustomerCode)).ToDictionaryAsync(c => c.CustomerCode, cancellationToken);
        var fileCustomers = targets.ToDictionary(r => r.CustomerCode, r => r.ToCustomer(string.Empty, default));

        var reasons = new Dictionary<int, string>();
        foreach (var row in targets)
        {
            var reason =
                registered.Contains(row.CustomerCode) ? "既に登録されている得意先コードです。"
                : row.SalesEmployeeCode is not null && !validEmployees.Contains(row.SalesEmployeeCode) ? $"営業担当社員コード「{row.SalesEmployeeCode}」が社員マスタにありません。"
                : row.BankAccountCode1 is not null && !validBanks.Contains(row.BankAccountCode1) ? $"振込先口座1「{row.BankAccountCode1}」が口座マスタにありません。"
                : row.BankAccountCode2 is not null && !validBanks.Contains(row.BankAccountCode2) ? $"振込先口座2「{row.BankAccountCode2}」が口座マスタにありません。"
                : BillingAggregationValidator.Validate(
                    fileCustomers[row.CustomerCode],
                    dbBillingTargets.GetValueOrDefault(row.BillingCustomerCode) ?? fileCustomers.GetValueOrDefault(row.BillingCustomerCode));
            if (reason is not null)
            {
                reasons[row.RowNumber] = reason;
            }
        }

        // 請求集約先がエラーで取り込まれない場合、その集約元も取り込めない（集約は2段階まで＝連鎖は1回で尽きる）。
        var failedCodes = targets.Where(r => reasons.ContainsKey(r.RowNumber)).Select(r => r.CustomerCode).ToHashSet();
        foreach (var row in targets.Where(r => !reasons.ContainsKey(r.RowNumber)
            && r.BillingCustomerCode != r.CustomerCode
            && failedCodes.Contains(r.BillingCustomerCode)
            && !dbBillingTargets.ContainsKey(r.BillingCustomerCode)))
        {
            reasons[row.RowNumber] = $"請求得意先コード「{row.BillingCustomerCode}」の行がエラーのため取り込めません。";
        }

        return rows.Select(r => reasons.TryGetValue(r.RowNumber, out var reason)
            && r.Status != CustomerImportStatus.Error
                ? r with { Status = CustomerImportStatus.Error, Reason = reason }
                : r).ToList();
    }

    /// <summary>
    /// エラー以外の行を新規登録する。実行直前にDBを見直し、プレビュー後に新たなエラーが出た場合は何も登録せず例外にする。
    /// 請求集約先と集約元は同一 SaveChanges で挿入できない（EFが複合FKを認識できない）ため、
    /// まず全件を「請求得意先＝自分自身」で登録し、次に集約元の請求得意先コードを更新する（1トランザクション）。
    /// </summary>
    public async Task<int> ImportAsync(IReadOnlyList<CustomerImportRow> rows, CancellationToken cancellationToken = default)
    {
        var targets = rows.Where(r => r.Status != CustomerImportStatus.Error).ToList();
        var recheck = await ValidateAgainstDbAsync(targets, cancellationToken);
        if (recheck.FirstOrDefault(r => r.Status == CustomerImportStatus.Error) is { } bad)
        {
            throw new CustomerValidationException($"{bad.RowNumber}行目: {bad.Reason}（読込後にデータが変わりました。読み込み直してください）");
        }

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        var customers = targets.Select(r =>
        {
            var customer = r.ToCustomer(employeeCode, now);
            customer.BillingCustomerCode = customer.CustomerCode;
            return customer;
        }).ToList();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            dbContext.Customers.AddRange(customers);
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var (customer, row) in customers.Zip(targets).Where(p => p.Second.BillingCustomerCode != p.Second.CustomerCode))
            {
                customer.BillingCustomerCode = row.BillingCustomerCode;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // 失敗した追加・変更を追跡に残すと、再実行時に二重に登録しようとする。
            dbContext.ChangeTracker.Clear();
            throw;
        }

        logger.LogInformation("得意先マスタをExcelから取り込みました。件数={Count}", customers.Count);
        return customers.Count;
    }
}
