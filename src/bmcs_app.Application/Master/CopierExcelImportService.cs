using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>プレビュー行の判定状態。</summary>
public enum CopierImportStatus
{
    /// <summary>未登録の機番。新規登録する。</summary>
    New,
    /// <summary>登録済み（無効化済みを含む）の機番。得意先コード・備考を上書きし、有効にする。</summary>
    Update,
    /// <summary>取込不可（<see cref="CopierImportRow.Reason"/> に理由）。</summary>
    Error,
}

/// <summary>Excelの1行。<see cref="CustomerName"/> はDBとの突き合わせで得意先が見つかったときだけ入る。</summary>
public sealed record CopierImportRow(
    int RowNumber,
    string MachineNo,
    string CustomerCode,
    string? Remarks,
    string? CustomerName,
    CopierImportStatus Status,
    string? Reason);

/// <summary>読込結果。<see cref="FileError"/> が非nullならファイル全体が取込不可。</summary>
public sealed record CopierImportReadResult(string? FileError, IReadOnlyList<CopierImportRow> Rows);

/// <summary>
/// コピー機マスタのExcel取込（docs/design_document.md 40章）。1行目の見出し名で列を特定し、
/// 全件を1トランザクションで登録する。登録済みの機番は得意先コード・備考を上書きする。
/// </summary>
public class CopierExcelImportService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<CopierExcelImportService> logger)
{
    /// <summary>テンプレートは2〜4行目が説明・入力例のため、データは5行目から読む。</summary>
    private const int FirstDataRow = 5;

    private static readonly string[] RequiredHeaders = ["機番", "得意先コード"];

    /// <summary>Excelを読み、行ごとに検証して新規／更新／エラーを判定する。</summary>
    public async Task<CopierImportReadResult> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        // Excelで開いたままでも読めるよう、共有読み取りで開く。
        var parsed = await Task.Run(() =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Parse(stream);
        }, cancellationToken);
        return parsed.FileError is not null
            ? parsed
            : new CopierImportReadResult(null, await ValidateAgainstDbAsync(parsed.Rows, cancellationToken));
    }

    /// <summary>ブックを解析する（DBを見ない）。見出しが揃った最初のシートを対象にする。</summary>
    public static CopierImportReadResult Parse(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        return ExcelImportReader.FindSheet(workbook, RequiredHeaders) is var (sheet, columns)
            ? new CopierImportReadResult(null, ParseRows(sheet, columns))
            : new CopierImportReadResult(ExcelImportReader.MissingHeadersMessage(RequiredHeaders), []);
    }

    private static List<CopierImportRow> ParseRows(IXLWorksheet sheet, Dictionary<string, int> columns)
    {
        var rows = new List<CopierImportRow>();
        var seen = new HashSet<string>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = FirstDataRow; r <= lastRow; r++)
        {
            var machineNo = ExcelImportReader.Text(sheet, columns, r, "機番");
            var customerCode = ExcelImportReader.Text(sheet, columns, r, "得意先コード");
            var remarks = ExcelImportReader.Text(sheet, columns, r, "備考") is { Length: > 0 } t ? t : null;
            if (machineNo.Length == 0 && customerCode.Length == 0 && remarks is null)
            {
                continue; // 書式・数式だけ残った空行
            }

            string? error =
                machineNo.Length == 0 ? "機番が空です。"
                : machineNo.Length > 20 || !machineNo.All(ch => ch is > ' ' and < '\u007f') ? "機番は半角（英数記号）20文字以内にしてください。"
                : !seen.Add(machineNo) ? "機番がファイル内で重複しています。"
                : customerCode.Length == 0 ? "得意先コードが空です。"
                : customerCode.Length > 10 ? "得意先コードが10文字を超えています。"
                : remarks?.Length > 100 ? "備考が100文字を超えています。"
                : null;

            rows.Add(new CopierImportRow(r, machineNo, customerCode, remarks, null,
                error is null ? CopierImportStatus.New : CopierImportStatus.Error, error));
        }

        return rows;
    }

    /// <summary>
    /// DBとの突き合わせ（機番の登録済み・得意先の有効性）。解析でエラーでない行だけを見て、新規／更新を決め、
    /// 新たにエラーになった行は理由つきで返す。プレビューと取込実行の両方から呼ぶ。
    /// </summary>
    private async Task<List<CopierImportRow>> ValidateAgainstDbAsync(IReadOnlyList<CopierImportRow> rows, CancellationToken cancellationToken)
    {
        var targets = rows.Where(r => r.Status != CopierImportStatus.Error).ToList();
        var machineNos = targets.Select(r => r.MachineNo).ToList();
        var customerCodes = targets.Select(r => r.CustomerCode).Distinct().ToList();

        var registered = (await dbContext.CopierMachines.AsNoTracking()
            .Where(m => machineNos.Contains(m.MachineNo)).Select(m => m.MachineNo).ToListAsync(cancellationToken)).ToHashSet();
        var activeCustomers = await dbContext.Customers.AsNoTracking()
            .Where(c => customerCodes.Contains(c.CustomerCode) && !c.IsDeleted)
            .ToDictionaryAsync(c => c.CustomerCode, c => c.CustomerName, cancellationToken);

        return rows.Select(r =>
        {
            if (r.Status == CopierImportStatus.Error)
            {
                return r;
            }

            return activeCustomers.TryGetValue(r.CustomerCode, out var name)
                ? r with { CustomerName = name, Status = registered.Contains(r.MachineNo) ? CopierImportStatus.Update : CopierImportStatus.New }
                : r with { Status = CopierImportStatus.Error, Reason = $"得意先コード「{r.CustomerCode}」は登録されていないか、無効です。" };
        }).ToList();
    }

    /// <summary>
    /// エラー以外の行を新規登録・更新する。実行直前にDBを見直し、プレビュー後に新たなエラーが出た場合は何も登録せず例外にする。
    /// 新規か更新かは見直した結果で決める（プレビュー後に他の端末で登録された機番は更新になる）。
    /// </summary>
    public async Task<(int Inserted, int Updated)> ImportAsync(IReadOnlyList<CopierImportRow> rows, CancellationToken cancellationToken = default)
    {
        var targets = rows.Where(r => r.Status != CopierImportStatus.Error).ToList();
        var recheck = await ValidateAgainstDbAsync(targets, cancellationToken);
        if (recheck.FirstOrDefault(r => r.Status == CopierImportStatus.Error) is { } bad)
        {
            throw new CopierMachineValidationException($"{bad.RowNumber}行目: {bad.Reason}（読込後にデータが変わりました。読み込み直してください）");
        }

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        var machineNos = recheck.Select(r => r.MachineNo).ToList();
        var existing = await dbContext.CopierMachines.Where(m => machineNos.Contains(m.MachineNo))
            .ToDictionaryAsync(m => m.MachineNo, cancellationToken);

        foreach (var row in recheck)
        {
            if (existing.TryGetValue(row.MachineNo, out var machine))
            {
                machine.CustomerCode = row.CustomerCode;
                machine.Remarks = row.Remarks;
                machine.IsDeleted = false;
                machine.UpdatedBy = employeeCode;
                machine.UpdatedAt = now;
                continue;
            }

            dbContext.CopierMachines.Add(new CopierMachine
            {
                MachineNo = row.MachineNo,
                CustomerCode = row.CustomerCode,
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
            throw new CopierMachineConcurrencyException("他のユーザーが更新しました。読み込み直してください。", ex);
        }
        finally
        {
            // 失敗した追加・変更を追跡に残すと、再実行時に二重に登録しようとする。
            dbContext.ChangeTracker.Clear();
        }

        var updated = existing.Count;
        logger.LogInformation("コピー機マスタをExcelから取り込みました。新規={Inserted} 更新={Updated}", recheck.Count - updated, updated);
        return (recheck.Count - updated, updated);
    }
}
