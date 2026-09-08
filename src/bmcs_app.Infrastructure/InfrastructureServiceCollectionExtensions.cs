using bmcs_app.Infrastructure.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Infrastructure;

/// <summary>
/// データアクセス層の DI 登録。
/// Presentation 層からは直接呼ばず、Application 層の AddApplication() 経由で呼ばれる
/// （Presentation が Infrastructure を参照しないため。docs/architecture.md 2章）。
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>設定ファイル上の接続文字列のキー名。</summary>
    public const string ConnectionStringName = "BmcsDb";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"接続文字列 '{ConnectionStringName}' が設定されていません。" +
                "appsettings.Development.json を appsettings.Development.json.sample から作成してください。");
        }

        // Scoped で登録する。ウィンドウごとにスコープを作るため、
        // 画面をまたいで DbContext が共有されない（docs/architecture.md 4章）。
        // UseSnakeCaseNamingConvention: PascalCase(C#) ↔ snake_case(DB) の変換を自動化する
        // （docs/database-schema.md の命名規則）。列ごとに HasColumnName を書かずに済む。
        services.AddDbContext<BmcsDbContext>(
            options => options
                .UseSqlServer(connectionString)
                .UseSnakeCaseNamingConvention(),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);

        // DbContext と同じ Scoped で登録する（TODO.md 4-1）。
        services.AddScoped<SlipNumberSequenceCommand>();

        return services;
    }
}
