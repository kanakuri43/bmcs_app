using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using bmcs_app.Application;
using bmcs_app.Services;
using bmcs_app.ViewModels.Common;
using bmcs_app.ViewModels.Master;
using bmcs_app.ViewModels.Menu;
using bmcs_app.ViewModels.Order;
using bmcs_app.ViewModels.Sales;
using bmcs_app.Views.Common;
using bmcs_app.Views.Master;
using bmcs_app.Views.Menu;
using bmcs_app.Views.Order;
using bmcs_app.Views.Sales;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace bmcs_app;

/// <summary>
/// アプリケーションのエントリポイント。DI・設定・ロギング・例外ハンドリングを構成する。
/// 起動時パラメータ（社員コード）の解析と権限判定は Phase 0-7 で追加する。
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    /// <summary>ログの出力先。端末ローカルに置き、DB では管理しない。</summary>
    private static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "bmcs_app",
            "logs");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = BuildHost();
            await _host.StartAsync();

            RegisterGlobalExceptionHandlers();

            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            logger.LogInformation("アプリケーションを起動しました。");

            _host.Services
                .GetRequiredService<WindowService>()
                .Show<MainMenuWindow, MainMenuViewModel>();
        }
        catch (Exception ex)
        {
            // 起動処理中の失敗はロガーが未構成の可能性があるため、直接表示して終了する。
            MessageBox.Show(
                $"起動に失敗しました。{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "bmcs_app",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.Services.GetRequiredService<ILogger<App>>()
                .LogInformation("アプリケーションを終了します。");

            await _host.StopAsync();
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        // 実行ディレクトリを基準に設定を読む（WPF の既定は異なるため明示する）。
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);

        // 出力レベルは appsettings.json の Serilog:MinimumLevel で制御する
        // （現場でログレベルを上げ下げできるようにするため、コード埋め込みにしない）。
        // 出力先だけは %LOCALAPPDATA% を解決する必要があるためコード側で指定する。
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.File(
                Path.Combine(LogDirectory, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                outputTemplate: "{Timestamp:yyyy/MM/dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);

        // Application 層の登録。内部で Infrastructure も登録されるため、
        // Presentation は Infrastructure を参照しない（docs/architecture.md 2章）。
        builder.Services.AddApplication(builder.Configuration);

        builder.Services.AddSingleton<WindowService>();

        // ウィンドウと ViewModel は Scoped。WindowService がウィンドウごとに
        // スコープを作るため、同じウィンドウ内では同じ DbContext を共有し、
        // ウィンドウ間では共有されない。
        builder.Services.AddScoped<MainMenuWindow>();
        builder.Services.AddScoped<MainMenuViewModel>();

        builder.Services.AddScoped<CustomerMasterWindow>();
        builder.Services.AddScoped<CustomerMasterViewModel>();

        builder.Services.AddScoped<ProductMasterWindow>();
        builder.Services.AddScoped<ProductMasterViewModel>();

        builder.Services.AddScoped<PrinterSettingsWindow>();
        builder.Services.AddScoped<PrinterSettingsViewModel>();

        builder.Services.AddScoped<CustomerSearchDialog>();
        builder.Services.AddScoped<CustomerSearchDialogViewModel>();

        builder.Services.AddScoped<ProductSearchDialog>();
        builder.Services.AddScoped<ProductSearchDialogViewModel>();

        builder.Services.AddScoped<ProductMasterSearchDialog>();
        builder.Services.AddScoped<ProductMasterSearchDialogViewModel>();

        builder.Services.AddScoped<SlipSearchDialog>();
        builder.Services.AddScoped<SlipSearchDialogViewModel>();

        builder.Services.AddScoped<OrderEntryWindow>();
        builder.Services.AddScoped<OrderEntryViewModel>();

        builder.Services.AddScoped<SalesEntryWindow>();
        builder.Services.AddScoped<SalesEntryViewModel>();

        return builder.Build();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        // UI スレッドの未処理例外。ログを残して継続する。
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // UI スレッド以外の未処理例外。継続できないためログのみ残す。
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.ForContext<App>().Fatal(args.ExceptionObject as Exception, "処理されない例外が発生しました。");
            Log.CloseAndFlush();
        };

        // 監視されなかった Task の例外。
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.ForContext<App>().Error(args.Exception, "監視されていない Task の例外が発生しました。");
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.ForContext<App>().Error(e.Exception, "画面操作中に例外が発生しました。");

        // ダイアログは Phase 0-5 で MahApps のものに差し替える。
        MessageBox.Show(
            $"処理中にエラーが発生しました。{Environment.NewLine}{Environment.NewLine}" +
            $"{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}" +
            $"詳細はログを確認してください。{Environment.NewLine}{LogDirectory}",
            "bmcs_app",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        // アプリは継続させる（入力途中のデータを失わせない）。
        e.Handled = true;
    }
}
