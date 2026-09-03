using bmcs_app.Application.Common;
using bmcs_app.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application;

/// <summary>
/// 業務処理層の DI 登録。
/// Infrastructure の登録もここから行うことで、Presentation 層は Infrastructure を
/// 参照せずに済む（docs/architecture.md 2章の規約）。
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddInfrastructure(configuration);

        // ユースケースは DbContext と同じ Scoped で登録する。
        // ウィンドウ単位のスコープ内で DbContext を共有させるため。
        services.AddScoped<DatabaseHealthService>();

        return services;
    }
}
