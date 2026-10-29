using System.Data;
using bmcs_app.Application.Common;
using Microsoft.Win32;

namespace bmcs_app.ViewModels.Analysis;

/// <summary>分析画面の一覧を、表示されている並び順のまま CSV（<see cref="QueryRunnerService.WriteCsv"/> と同じ形式）に保存する。</summary>
internal static class AnalysisCsvExporter
{
    /// <summary>保存先を選ばせて書き出す。キャンセルなら null、保存したら保存先のパスを返す。</summary>
    public static string? Save(string defaultFileName, string[] headers, IEnumerable<object?[]> rows)
    {
        var dialog = new SaveFileDialog
        {
            Title = "CSVファイル保存",
            Filter = "CSVファイル|*.csv",
            FileName = defaultFileName,
        };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var table = new DataTable();
        foreach (var header in headers)
        {
            table.Columns.Add(header);
        }

        foreach (var row in rows)
        {
            table.Rows.Add(row);
        }

        QueryRunnerService.WriteCsv(table, dialog.FileName);
        return dialog.FileName;
    }
}
