using System.Globalization;
using ClosedXML.Excel;

namespace bmcs_app.Application.Master;

/// <summary>マスタExcel取込（得意先・コピー機・単価掛率）が共有する、シート特定とセル読み取り。</summary>
internal static class ExcelImportReader
{
    /// <summary>
    /// 1行目の見出し名から列番号を引き、必須の見出しがすべて揃った最初のシートを返す。無ければ null。
    /// 同名の見出しが複数あれば左側を採る。
    /// </summary>
    public static (IXLWorksheet Sheet, Dictionary<string, int> Columns)? FindSheet(XLWorkbook workbook, string[] requiredHeaders)
    {
        foreach (var sheet in workbook.Worksheets)
        {
            var columns = sheet.Row(1).CellsUsed()
                .Select(c => (Header: Clean(c), c.Address.ColumnNumber))
                .Where(h => h.Header.Length > 0)
                .GroupBy(h => h.Header)
                .ToDictionary(g => g.Key, g => g.First().ColumnNumber);
            if (requiredHeaders.All(columns.ContainsKey))
            {
                return (sheet, columns);
            }
        }

        return null;
    }

    /// <summary>セルの値を文字列にして前後の空白（全角含む）を除く。数値は指数表記にしない。</summary>
    public static string Clean(IXLCell cell)
    {
        var value = cell.Value;
        var text = value.IsNumber ? ((decimal)value.GetNumber()).ToString("0.####", CultureInfo.InvariantCulture)
            : value.IsBlank ? string.Empty
            : value.IsText ? value.GetText()
            : value.ToString();
        return text.Trim(' ', '　');
    }

    /// <summary>見出しの無い任意列は空文字として、行のセルを <see cref="Clean"/> して返す。</summary>
    public static string Text(IXLWorksheet sheet, Dictionary<string, int> columns, int row, string header)
        => columns.TryGetValue(header, out var c) ? Clean(sheet.Cell(row, c)) : string.Empty;

    /// <summary>必須列の見出しがファイルエラーになったときのメッセージ。</summary>
    public static string MissingHeadersMessage(string[] requiredHeaders)
        => $"見出し（{string.Join("・", requiredHeaders)}）が揃ったシートがありません。";
}
