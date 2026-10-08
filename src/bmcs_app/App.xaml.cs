using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using bmcs_app.Application;
using bmcs_app.Reports;
using bmcs_app.Services;
using bmcs_app.ViewModels.Billing;
using bmcs_app.ViewModels.Closing;
using bmcs_app.ViewModels.Common;
using bmcs_app.ViewModels.Ledger;
using bmcs_app.ViewModels.Master;
using bmcs_app.ViewModels.Menu;
using bmcs_app.ViewModels.Order;
using bmcs_app.ViewModels.Receipt;
using bmcs_app.ViewModels.Sales;
using bmcs_app.Views.Billing;
using bmcs_app.Views.Closing;
using bmcs_app.Views.Common;
using bmcs_app.Views.Ledger;
using bmcs_app.Views.Master;
using bmcs_app.Views.Menu;
using bmcs_app.Views.Order;
using bmcs_app.Views.Receipt;
using bmcs_app.Views.Sales;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace bmcs_app;

/// <summary>
/// アプリケーションのエントリポイント。DI・設定・ロギング・例外ハンドリングを構成する。
/// 起動時パラメータ（ショートカット引数の1つ目＝社員コード）は <see cref="OnStartup"/> で受け取り、
/// <c>StartupArgsCurrentEmployeeContext</c>へ渡す。権限レベルの判定はメニュー画面
/// が社員マスタを参照して行う。
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

        // コピー機売上CSV（Shift-JIS）を読むために必要。
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // DatePickerは FrameworkElement.Language 由来の
        // カルチャで表示書式（ShortDatePattern）を決める。既定は en-US のため、設定しないと
        // 「9/15/2026」表記になり docs/product-spec.md UI/UX「日付は yyyy/MM/dd 表記で統一」が
        // 崩れる。実行端末の OS 言語に依存させず ja-JP に固定する。どのウィンドウも
        // 生成される前（最初の1回だけ）に設定する必要がある。
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("ja-JP")));

        try
        {
            _host = BuildHost(e.Args);
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

    private static IHost BuildHost(string[] startupArgs)
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
        builder.Services.AddApplication(builder.Configuration, startupArgs);

        builder.Services.AddSingleton<WindowService>();

        // 帳票の印刷・PDF出力。PrinterSettingsService（Application）と同様、
        // DbContext に依存しないため Singleton。
        builder.Services.AddSingleton<ReportPrintService>();

        // ウィンドウと ViewModel は Scoped。WindowService がウィンドウごとに
        // スコープを作るため、同じウィンドウ内では同じ DbContext を共有し、
        // ウィンドウ間では共有されない。
        builder.Services.AddScoped<MainMenuWindow>();
        builder.Services.AddScoped<MainMenuViewModel>();

        builder.Services.AddScoped<CustomerMasterWindow>();
        builder.Services.AddScoped<CustomerMasterViewModel>();

        builder.Services.AddScoped<ProductMasterWindow>();
        builder.Services.AddScoped<ProductMasterViewModel>();

        builder.Services.AddScoped<EmployeeMasterWindow>();
        builder.Services.AddScoped<EmployeeMasterViewModel>();

        builder.Services.AddScoped<CompanyInfoSettingsWindow>();
        builder.Services.AddScoped<CompanyInfoSettingsViewModel>();

        builder.Services.AddScoped<MenuMasterWindow>();
        builder.Services.AddScoped<MenuMasterViewModel>();

        builder.Services.AddScoped<BankAccountMasterWindow>();
        builder.Services.AddScoped<BankAccountMasterViewModel>();

        builder.Services.AddScoped<CopierMachineMasterWindow>();
        builder.Services.AddScoped<CopierMachineMasterViewModel>();

        builder.Services.AddScoped<DepositMethodMasterWindow>();
        builder.Services.AddScoped<DepositMethodMasterViewModel>();

        builder.Services.AddScoped<PrinterSettingsWindow>();
        builder.Services.AddScoped<PrinterSettingsViewModel>();

        builder.Services.AddScoped<CustomerSearchDialog>();
        builder.Services.AddScoped<CustomerSearchDialogViewModel>();

        builder.Services.AddScoped<ProductSearchDialog>();
        builder.Services.AddScoped<ProductSearchDialogViewModel>();

        builder.Services.AddScoped<ProductMasterSearchDialog>();
        builder.Services.AddScoped<ProductMasterSearchDialogViewModel>();

        builder.Services.AddScoped<EmployeeMasterSearchDialog>();
        builder.Services.AddScoped<EmployeeMasterSearchDialogViewModel>();

        builder.Services.AddScoped<BankAccountMasterSearchDialog>();
        builder.Services.AddScoped<BankAccountMasterSearchDialogViewModel>();

        builder.Services.AddScoped<CopierMachineMasterSearchDialog>();
        builder.Services.AddScoped<CopierMachineMasterSearchDialogViewModel>();

        builder.Services.AddScoped<DepositMethodMasterSearchDialog>();
        builder.Services.AddScoped<DepositMethodMasterSearchDialogViewModel>();

        builder.Services.AddScoped<SlipSearchDialog>();
        builder.Services.AddScoped<SlipSearchDialogViewModel>();

        builder.Services.AddScoped<ReportPreviewDialog>();
        builder.Services.AddScoped<ReportPreviewDialogViewModel>();

        builder.Services.AddScoped<OrderEntryWindow>();
        builder.Services.AddScoped<OrderEntryViewModel>();

        builder.Services.AddScoped<SalesEntryWindow>();
        builder.Services.AddScoped<SalesEntryViewModel>();

        builder.Services.AddScoped<CopierSalesImportWindow>();
        builder.Services.AddScoped<CopierSalesImportViewModel>();

        builder.Services.AddScoped<BillingClosingWindow>();
        builder.Services.AddScoped<BillingClosingViewModel>();

        builder.Services.AddScoped<MonthlyClosingWindow>();
        builder.Services.AddScoped<MonthlyClosingViewModel>();
        builder.Services.AddScoped<MonthlyClosingReleaseWindow>();
        builder.Services.AddScoped<MonthlyClosingReleaseViewModel>();

        builder.Services.AddScoped<BillingReleaseWindow>();
        builder.Services.AddScoped<BillingReleaseViewModel>();

        builder.Services.AddScoped<DetailInvoiceIssueWindow>();
        builder.Services.AddScoped<DetailInvoiceIssueViewModel>();

        builder.Services.AddScoped<ReceiptEntryWindow>();
        builder.Services.AddScoped<ReceiptEntryViewModel>();

        builder.Services.AddScoped<DetailReceiptEntryWindow>();
        builder.Services.AddScoped<DetailReceiptEntryViewModel>();

        builder.Services.AddScoped<CustomerLedgerWindow>();
        builder.Services.AddScoped<CustomerLedgerViewModel>();

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

        // ダイアログは今後 MahApps のものに差し替える予定。
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
