using System.Globalization;

namespace bmcs_app.Domain.Import;

/// <summary>CSV1行の解析結果（成功）。</summary>
public sealed record CopierCsvRow(
    int LineNumber,
    string MachineNo,
    string ModelName,
    decimal AmountExcludingTax,
    DateOnly ClosingDate,
    string SlipRemarks,
    string InternalRemarks);

/// <summary>CSV1行の解析結果。<see cref="Row"/> と <see cref="Error"/> のどちらか一方のみ非null。</summary>
public sealed record CopierCsvLineResult(int LineNumber, CopierCsvRow? Row, string? Error);

/// <summary>ファイル全体の解析結果。<see cref="FileError"/> が非nullなら <see cref="Lines"/> は空。</summary>
public sealed record CopierCsvParseResult(string? FileError, IReadOnlyList<CopierCsvLineResult> Lines);

/// <summary>
/// コピー機売上CSV（デコード済み全文）の解析。列はヘッダーの列名で引く（並び・追加列に非依存）。
/// カンマ区切り・引用符なし。純粋関数のみでファイルIOは行わない。
/// </summary>
public static class CopierCsvParser
{
    public const int RemarksMaxLength = 200;

    private const string ColMachineNo = "機番";
    private const string ColModel = "機種名（漢字）";
    private const string ColCvMono = "ユーザ請求CV（モノ）";
    private const string ColCvFull = "ユーザ請求CV（フル）";
    private const string ColCvFullP = "ユーザ請求CV（フルＰ）";
    private const string ColAmount = "ユーザー請求金額（機器合計）-税別";
    private const string ColClosing = "締日";

    private static readonly string[] RequiredColumns =
        [ColMachineNo, ColModel, ColCvMono, ColCvFull, ColCvFullP, ColAmount, ColClosing];

    public static CopierCsvParseResult Parse(string text)
    {
        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

        // ヘッダー行（最初の非空行）
        var h = Array.FindIndex(lines, l => !IsBlank(l));
        if (h < 0) return new("ヘッダー行がありません。", []);

        var headers = lines[h].TrimStart('\uFEFF').Split(',');
        var index = new Dictionary<string, int>();
        for (var i = 0; i < headers.Length; i++) index.TryAdd(headers[i].Trim(), i);

        var missing = RequiredColumns.Where(c => !index.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            return new($"必須列がありません: {string.Join("、", missing)}", []);

        var results = new List<CopierCsvLineResult>();
        for (var i = h + 1; i < lines.Length; i++)
        {
            if (IsBlank(lines[i])) continue;
            var no = i + 1;
            var cells = lines[i].Split(',');
            if (cells.Length < headers.Length)
            {
                results.Add(new(no, null, $"列数が不足しています（{cells.Length}列。ヘッダーは{headers.Length}列）。"));
                continue;
            }
            string Cell(string col) => cells[index[col]].Trim();

            var machineNo = Cell(ColMachineNo);
            if (machineNo.Length == 0) { results.Add(new(no, null, "機番が空です。")); continue; }

            if (!decimal.TryParse(Cell(ColAmount), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var amount))
            { results.Add(new(no, null, $"金額が不正です: {Cell(ColAmount)}")); continue; }

            if (!DateOnly.TryParseExact(Cell(ColClosing), "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var closing))
            { results.Add(new(no, null, $"締日が不正です: {Cell(ColClosing)}")); continue; }

            var model = Cell(ColModel);
            var remarks = $"{model} モノ{Cv(Cell(ColCvMono))} フル{Cv(Cell(ColCvFull))} フルP{Cv(Cell(ColCvFullP))}";
            if (remarks.Length > RemarksMaxLength)
            { results.Add(new(no, null, $"社外摘要が{RemarksMaxLength}字を超えています。")); continue; }
            if (machineNo.Length > RemarksMaxLength)
            { results.Add(new(no, null, $"社内摘要（機番）が{RemarksMaxLength}字を超えています。")); continue; }

            results.Add(new(no, new CopierCsvRow(no, machineNo, model, amount, closing, remarks, machineNo), null));
        }
        return new(null, results);
    }

    private static string Cv(string v) => v.Length == 0 ? "0" : v;

    private static bool IsBlank(string line) => line.Trim().Trim(',').Length == 0;
}
