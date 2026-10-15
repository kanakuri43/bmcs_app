using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using bmcs_app.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Common;

/// <summary>クエリ定義のパラメータ1件。<c>#param{Id}</c> を <see cref="Value"/> で文字列置換して実行する。</summary>
public sealed record QueryParam(int Id, string Name, string Value);

/// <summary>クエリファイル（<c>bmcs_queries.xml</c>）の1クエリ。</summary>
public sealed record QueryDefinition(int Id, string Name, string Sql, IReadOnlyList<QueryParam> Params);

/// <summary>
/// 汎用クエリ実行。クエリは端末ローカルの <c>bmcs_queries.xml</c>（実行ファイルと同じフォルダ）に書く。
/// 形式は <c>&lt;Query id&gt;&lt;Name/&gt;&lt;SQL/&gt;&lt;Param id&gt;&lt;Name/&gt;&lt;Value/&gt;&lt;/Param&gt;&lt;/Query&gt;</c>。
/// SQL 中の <c>#param{id}</c> はパラメータ値の<b>文字列置換</b>（引用符も値に含める。例: <c>'20250531'</c>）。
/// 任意のSQLを実行できるため、呼び出し側のメニューを管理者権限に限る前提。
/// </summary>
public partial class QueryRunnerService(BmcsDbContext dbContext, ILogger<QueryRunnerService> logger)
{
    private const int CommandTimeoutSeconds = 60;

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "bmcs_queries.xml");

    private const string Template = """
        <?xml version="1.0" encoding="utf-8"?>
        <bmcsQueries>
          <Query id="0">
            <Name>サンプル（得意先一覧）</Name>
            <SQL>SELECT TOP (#param0) * FROM dbo.customers</SQL>
            <Param id="0">
              <Name>件数</Name>
              <Value>10</Value>
            </Param>
          </Query>
        </bmcsQueries>
        """;

    /// <summary>クエリファイルを読む。無ければサンプル入りで新規作成する。</summary>
    public IReadOnlyList<QueryDefinition> Load()
    {
        if (!File.Exists(FilePath))
        {
            File.WriteAllText(FilePath, Template, new UTF8Encoding(false));
        }

        return XDocument.Load(FilePath).Root!.Elements("Query")
            .Select(q => new QueryDefinition(
                (int)q.Attribute("id")!,
                (string?)q.Element("Name") ?? string.Empty,
                (string?)q.Element("SQL") ?? string.Empty,
                q.Elements("Param")
                    .Select(p => new QueryParam((int)p.Attribute("id")!, (string?)p.Element("Name") ?? string.Empty, (string?)p.Element("Value") ?? string.Empty))
                    .ToList()))
            .ToList();
    }

    /// <summary>実行したクエリのパラメータ値をクエリファイルへ保存する（次回の初期値）。</summary>
    public void SaveParamValues(int queryId, IReadOnlyDictionary<int, string> values)
    {
        var doc = XDocument.Load(FilePath);
        var query = doc.Root!.Elements("Query").First(q => (int)q.Attribute("id")! == queryId);
        foreach (var p in query.Elements("Param"))
        {
            if (values.TryGetValue((int)p.Attribute("id")!, out var v))
            {
                p.SetElementValue("Value", v);
            }
        }

        doc.Save(FilePath);
    }

    public async Task<DataTable> RunAsync(QueryDefinition query, IReadOnlyDictionary<int, string> values, CancellationToken cancellationToken = default)
    {
        var sql = ParamRegex().Replace(query.Sql, m =>
            values.TryGetValue(int.Parse(m.Groups[1].Value), out var v) ? v : m.Value);
        logger.LogInformation("クエリ「{Name}」を実行します。", query.Name);

        await using var connection = new SqlConnection(dbContext.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        var table = new DataTable();
        await Task.Run(() => new SqlDataAdapter(command).Fill(table), cancellationToken);
        return table;
    }

    /// <summary>結果を Shift_JIS・ヘッダー付きの CSV に書く。起動時に <c>CodePagesEncodingProvider</c> の登録が必要。</summary>
    public static void WriteCsv(DataTable table, string path)
    {
        using var writer = new StreamWriter(path, false, Encoding.GetEncoding("Shift_JIS"));
        writer.Write(string.Join(',', table.Columns.Cast<DataColumn>().Select(c => Quote(c.ColumnName))));
        writer.Write("\r\n");
        foreach (DataRow row in table.Rows)
        {
            writer.Write(string.Join(',', row.ItemArray.Select(v => Quote(Convert.ToString(v) ?? string.Empty))));
            writer.Write("\r\n");
        }
    }

    private static string Quote(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0 || field.StartsWith(' ') || field.EndsWith(' ') || field.StartsWith('\t') || field.EndsWith('\t')
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;

    [GeneratedRegex(@"#param(\d+)")]
    private static partial Regex ParamRegex();
}
