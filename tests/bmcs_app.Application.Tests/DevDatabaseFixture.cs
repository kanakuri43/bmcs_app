using bmcs_app.Application;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests;

/// <summary>
/// 開発用ライブDB（172.16.3.171）に対する結合テストの共通フィクスチャ（TODO.md 4-1）。
/// <see cref="ApplicationServiceCollectionExtensions.AddApplication"/> を通してサービスを
/// 解決するため、DI 配線も含めて検証できる。
/// </summary>
/// <remarks>
/// テスト専用の使い捨てキー（<see cref="TestSequenceKey"/>）を <see cref="InitializeAsync"/>
/// で冪等に作成し、<see cref="DisposeAsync"/> で削除する。業務キー（<c>order_slip</c> 等）の
/// <c>current_value</c> は一切変更しない。前回実行がクラッシュして後始末できていなくても、
/// DELETE→INSERT の順で作り直すため再実行できる。
/// </remarks>
public sealed class DevDatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// テスト用の使い捨て採番キー。<see cref="bmcs_app.Domain.Enums.SlipNumberKind"/> の
    /// どの実キーとも衝突しないよう "__" プレフィックスにする。
    /// </summary>
    public const string TestSequenceKey = "__test_slip_number";

    private ServiceProvider? _serviceProvider;

    public IServiceProvider Services => _serviceProvider
        ?? throw new InvalidOperationException("InitializeAsync が呼ばれていません。");

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(
                "appsettings.Development.json",
                optional: false)
            .Build();

        var services = new ServiceCollection();
        services.AddApplication(configuration);
        _serviceProvider = services.BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();

        await dbContext.Database.ExecuteSqlAsync(
            $"DELETE FROM dbo.slip_number_sequence WHERE sequence_key = {TestSequenceKey}");
        await dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO dbo.slip_number_sequence
                (sequence_key, current_value, created_by, created_at, updated_by, updated_at)
            VALUES ({TestSequenceKey}, 0, N'TEST', SYSDATETIME(), N'TEST', SYSDATETIME())
            """);
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        await dbContext.Database.ExecuteSqlAsync(
            $"DELETE FROM dbo.slip_number_sequence WHERE sequence_key = {TestSequenceKey}");

        await _serviceProvider.DisposeAsync();
    }
}
