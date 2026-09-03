using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Common;

/// <summary>
/// DB 接続の動作確認用ユースケース。
/// Phase 0-3 時点ではテーブルが未作成のため、スキーマに依存しない情報のみを扱う。
/// </summary>
public class DatabaseHealthService(BmcsDbContext dbContext, ILogger<DatabaseHealthService> logger)
{
    /// <summary>
    /// この処理が使用している DbContext のインスタンス識別子。
    /// ウィンドウごとにスコープが分かれていることの確認用。
    /// </summary>
    public Guid DbContextInstanceId => dbContext.InstanceId;

    /// <summary>接続先の情報を取得する。</summary>
    public async Task<DatabaseInfo> GetDatabaseInfoAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "DB 情報を取得します。DbContext={InstanceId}", dbContext.InstanceId);

        var version = await dbContext.GetServerVersionAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();

        logger.LogInformation("DB 情報を取得しました。Database={Database}", connection.Database);

        return new DatabaseInfo(
            DataSource: connection.DataSource,
            Database: connection.Database,
            ServerVersion: version.ReplaceLineEndings(" ").Trim());
    }
}

/// <summary>接続先データベースの情報。</summary>
public record DatabaseInfo(string DataSource, string Database, string ServerVersion);
