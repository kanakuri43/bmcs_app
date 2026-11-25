using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>プレビュー行の判定状態。</summary>
public enum SupplierImportStatus
{
    /// <summary>未登録の仕入先コード。新規登録する。</summary>
    New,
    /// <summary>登録済み（無効化済みを含む）の仕入先コード。全項目をExcelの値で上書きし、有効にする。</summary>
    Update,
    /// <summary>取込不可（<see cref="SupplierImportRow.Reason"/> に理由）。</summary>
    Error,
}

/// <summary>Excelの1行。任意項目は空欄なら null。</summary>
public sealed record SupplierImportRow(
    int RowNumber,
    string SupplierCode,
    string SupplierName,
    string? SupplierNameKana,
    string? PostalCode,
    string? Address1,
    string? Address2,
    string? PhoneNumber,
    string? FaxNumber,
    string? ContactPersonName,
    string? Remarks,
    SupplierImportStatus Status,
    string? Reason);

/// <summary>読込結果。<see cref="FileError"/> が非nullならファイル全体が取込不可。</summary>
public sealed record SupplierImportReadResult(string? FileError, IReadOnlyList<SupplierImportRow> Rows);

/// <summary>
/// 仕入先マスタのExcel取込（docs/design_document.md 42章）。1行目の見出し名で列を特定し、
/// 全件を1トランザクションで登録する。登録済みの仕入先コードは全項目を上書きする。
/// </summary>
public class SupplierExcelImportService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<SupplierExcelImportService> logger)
{
    /// <summary>テンプレートは2〜4行目が説明・入力例のため、データは5行目から読む。</summary>
    private const int FirstDataRow = 5;

    private static readonly string[] RequiredHeaders = ["仕入先コード", "仕入先名"];

    /// <summary>Excelを読み、行ごとに検証して新規／更新／エラーを判定する。</summary>
    public async Task<SupplierImportReadResult> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        // Excelで開いたままでも読めるよう、共有読み取りで開く。
        var parsed = await Task.Run(() =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Parse(stream);
        }, cancellationToken);
        return parsed.FileError is not null
            ? parsed
            : new SupplierImportReadResult(null, await ValidateAgainstDbAsync(parsed.Rows, cancellationToken));
    }

    /// <summary>ブックを解析する（DBを見ない）。見出しが揃った最初のシートを対象にする。</summary>
    public static SupplierImportReadResult Parse(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        return ExcelImportReader.FindSheet(workbook, RequiredHeaders) is var (sheet, columns)
            ? new SupplierImportReadResult(null, ParseRows(sheet, columns))
            : new SupplierImportReadResult(ExcelImportReader.MissingHeadersMessage(RequiredHeaders), []);
    }

    private static List<SupplierImportRow> ParseRows(IXLWorksheet sheet, Dictionary<string, int> columns)
    {
        var rows = new List<SupplierImportRow>();
        var seen = new HashSet<string>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = FirstDataRow; r <= lastRow; r++)
        {
            // 見出しの無い任意列は空として扱う。
            string Text(string header) => ExcelImportReader.Text(sheet, columns, r, header);
            string? Opt(string header) => Text(header) is { Length: > 0 } t ? t : null;

            var code = Text("仕入先コード");
            var name = Text("仕入先名");
            string?[] optionals = [Opt("カナ"), Opt("郵便番号"), Opt("住所1"), Opt("住所2"), Opt("電話番号"), Opt("FAX番号"),
                Opt("先方担当者"), Opt("備考")];
            if (code.Length == 0 && name.Length == 0 && optionals.All(s => s is null))
            {
                continue; // 書式・数式だけ残った空行
            }

            var tooLong = new (string Label, string? Value, int Max)[]
                {
                    ("仕入先名", name, 60), ("カナ", optionals[0], 60), ("郵便番号", optionals[1], 8), ("住所1", optionals[2], 100),
                    ("住所2", optionals[3], 100), ("電話番号", optionals[4], 20), ("FAX番号", optionals[5], 20),
                    ("先方担当者", optionals[6], 40), ("備考", optionals[7], 100),
                }
                .FirstOrDefault(f => f.Value?.Length > f.Max);

            string? error =
                code.Length == 0 ? "仕入先コードが空です。"
                : code.Length > 10 || !code.All(ch => ch is > ' ' and < '\u007f') ? "仕入先コードは半角（英数記号）10文字以内にしてください。"
                : !seen.Add(code) ? "仕入先コードがファイル内で重複しています。"
                : name.Length == 0 ? "仕入先名が空です。"
                : tooLong.Value is not null ? $"{tooLong.Label}が{tooLong.Max}文字を超えています。"
                : null;

            rows.Add(new SupplierImportRow(r, code, name, optionals[0], optionals[1], optionals[2], optionals[3], optionals[4],
                optionals[5], optionals[6], optionals[7],
                error is null ? SupplierImportStatus.New : SupplierImportStatus.Error, error));
        }

        return rows;
    }

    /// <summary>
    /// DBとの突き合わせ（仕入先コードの登録済み）。解析でエラーでない行だけを見て、新規／更新を決める。
    /// プレビューと取込実行の両方から呼ぶ。仕入先はDBに依存する検証が無いため、エラーは増えない。
    /// </summary>
    private async Task<List<SupplierImportRow>> ValidateAgainstDbAsync(IReadOnlyList<SupplierImportRow> rows, CancellationToken cancellationToken)
    {
        var codes = rows.Where(r => r.Status != SupplierImportStatus.Error).Select(r => r.SupplierCode).ToList();
        var registered = (await dbContext.Suppliers.AsNoTracking()
            .Where(s => codes.Contains(s.SupplierCode)).Select(s => s.SupplierCode).ToListAsync(cancellationToken)).ToHashSet();
        return rows.Select(r => r.Status == SupplierImportStatus.Error
            ? r
            : r with { Status = registered.Contains(r.SupplierCode) ? SupplierImportStatus.Update : SupplierImportStatus.New }).ToList();
    }

    /// <summary>
    /// エラー以外の行を新規登録・更新する。新規か更新かは実行直前に登録状況を見直して決める
    /// （プレビュー後に他の端末で登録された仕入先は更新になる）。1回の SaveChanges で保存する。
    /// </summary>
    public async Task<(int Inserted, int Updated)> ImportAsync(IReadOnlyList<SupplierImportRow> rows, CancellationToken cancellationToken = default)
    {
        var targets = rows.Where(r => r.Status != SupplierImportStatus.Error).ToList();
        var codes = targets.Select(r => r.SupplierCode).ToList();
        var existing = await dbContext.Suppliers.Where(s => codes.Contains(s.SupplierCode))
            .ToDictionaryAsync(s => s.SupplierCode, cancellationToken);

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        foreach (var row in targets)
        {
            if (existing.TryGetValue(row.SupplierCode, out var supplier))
            {
                supplier.SupplierName = row.SupplierName;
                supplier.SupplierNameKana = row.SupplierNameKana;
                supplier.PostalCode = row.PostalCode;
                supplier.Address1 = row.Address1;
                supplier.Address2 = row.Address2;
                supplier.PhoneNumber = row.PhoneNumber;
                supplier.FaxNumber = row.FaxNumber;
                supplier.ContactPersonName = row.ContactPersonName;
                supplier.Remarks = row.Remarks;
                supplier.IsDeleted = false;
                supplier.UpdatedBy = employeeCode;
                supplier.UpdatedAt = now;
                continue;
            }

            dbContext.Suppliers.Add(new Supplier
            {
                SupplierCode = row.SupplierCode,
                SupplierName = row.SupplierName,
                SupplierNameKana = row.SupplierNameKana,
                PostalCode = row.PostalCode,
                Address1 = row.Address1,
                Address2 = row.Address2,
                PhoneNumber = row.PhoneNumber,
                FaxNumber = row.FaxNumber,
                ContactPersonName = row.ContactPersonName,
                Remarks = row.Remarks,
                CreatedBy = employeeCode,
                CreatedAt = now,
                UpdatedBy = employeeCode,
                UpdatedAt = now,
            });
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SupplierConcurrencyException("他のユーザーが更新しました。読み込み直してください。", ex);
        }
        finally
        {
            // 失敗した追加・変更を追跡に残すと、再実行時に二重に登録しようとする。
            dbContext.ChangeTracker.Clear();
        }

        logger.LogInformation("仕入先マスタをExcelから取り込みました。新規={Inserted} 更新={Updated}",
            targets.Count - existing.Count, existing.Count);
        return (targets.Count - existing.Count, existing.Count);
    }
}
