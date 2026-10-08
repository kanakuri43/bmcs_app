using System.Text;

namespace bmcs_app.Application.Sales;

/// <summary>コピー機売上CSVファイルをShift-JISで文字列に読む。起動時に <c>CodePagesEncodingProvider</c> の登録が必要。</summary>
public static class CopierCsvFileReader
{
    public static Task<string> ReadAsync(string path, CancellationToken ct = default) =>
        File.ReadAllTextAsync(path, Encoding.GetEncoding("shift_jis"), ct);
}
