using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Infrastructure;

/// <summary>
/// 販売管理システムの DbContext。
/// エンティティ定義とマッピング設定（PascalCase ↔ snake_case 等）は Phase 1-6 で追加する。
/// </summary>
public class BmcsDbContext(DbContextOptions<BmcsDbContext> options) : DbContext(options)
{
    /// <summary>
    /// このインスタンスの識別子。
    /// ウィンドウごとに DbContext のスコープが分かれていることを確認するために使う。
    /// </summary>
    public Guid InstanceId { get; } = Guid.NewGuid();

    /// <summary>
    /// サーバのバージョン文字列を取得する。テーブルが未作成でも実行できるため、
    /// 接続確認および層をまたいだ非同期アクセスの動作確認に使う。
    /// </summary>
    public async Task<string> GetServerVersionAsync(CancellationToken cancellationToken = default)
    {
        var versions = await Database
            .SqlQuery<string>($"SELECT @@VERSION AS Value")
            .ToListAsync(cancellationToken);

        return versions.FirstOrDefault() ?? string.Empty;
    }
}
