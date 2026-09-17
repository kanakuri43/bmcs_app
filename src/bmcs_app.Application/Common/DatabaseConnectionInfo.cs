using Microsoft.Data.SqlClient;

namespace bmcs_app.Application.Common;

/// <summary>
/// 現在接続しているSQL Serverインスタンス名・DB名（メインメニューのフッターに常時表示するため）。
/// 接続文字列（ConnectionStrings:BmcsDb）を <see cref="SqlConnectionStringBuilder"/> で解析するだけで、
/// 実際にDBへ接続することはない。
/// </summary>
public sealed class DatabaseConnectionInfo
{
    public DatabaseConnectionInfo(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        ServerName = builder.DataSource;
        DatabaseName = builder.InitialCatalog;
    }

    public string ServerName { get; }

    public string DatabaseName { get; }
}
