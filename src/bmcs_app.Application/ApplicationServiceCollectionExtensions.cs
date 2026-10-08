using bmcs_app.Application.Billing;
using bmcs_app.Application.Closing;
using bmcs_app.Application.Common;
using bmcs_app.Application.Ledger;
using bmcs_app.Application.Master;
using bmcs_app.Application.Order;
using bmcs_app.Application.Receipt;
using bmcs_app.Application.Sales;
using bmcs_app.Domain.Calculations;
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
        IConfiguration configuration,
        string[] startupArgs)
    {
        services.AddInfrastructure(configuration);

        // 接続文字列の解析のみ（DB接続は行わない）。AddInfrastructure が既に接続文字列の存在を検証済みのため、
        // ここでは null / 空文字にはならない。メインメニューのフッターに常時表示する接続先情報として使う。
        var connectionString = configuration.GetConnectionString(InfrastructureServiceCollectionExtensions.ConnectionStringName)!;
        services.AddSingleton(new DatabaseConnectionInfo(connectionString));

        // 現在操作している社員の解決。起動時パラメータ（ショートカット引数）の1つ目を社員コードとして扱う
        // （docs/architecture.md 14章）。
        services.AddSingleton<ICurrentEmployeeContext>(new StartupArgsCurrentEmployeeContext(startupArgs));

        // 端末ローカルのファイル I/O のみで DbContext に依存しないため Singleton。
        services.AddSingleton<PrinterSettingsService>();

        // 単価決定ロジック。将来的に掛け率マスタ等（DBアクセスを伴う実装）へ差し替える想定のため、
        // 現時点は状態を持たないが Scoped で登録しておく（docs/decisions.md 参照）。
        services.AddScoped<IUnitPriceCalculator, StandardUnitPriceCalculator>();

        // ユースケースは DbContext と同じ Scoped で登録する。
        // ウィンドウ単位のスコープ内で DbContext を共有させるため。
        services.AddScoped<CustomerService>();
        services.AddScoped<ProductService>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<CompanyInfoService>();
        services.AddScoped<BankAccountService>();
        services.AddScoped<DepositMethodService>();
        services.AddScoped<CopierMachineService>();
        services.AddScoped<MenuService>();
        services.AddScoped<ProductHistoryQueryService>();
        services.AddScoped<TaxRateQueryService>();
        services.AddScoped<SlipNumberService>();
        services.AddScoped<OrderService>();
        services.AddScoped<OrderStatusService>();
        services.AddScoped<OrderQueryService>();
        services.AddScoped<SalesService>();
        services.AddScoped<CopierSalesImportService>();
        services.AddScoped<SalesEditLockService>();
        services.AddScoped<SalesQueryService>();
        services.AddScoped<BillingClosingService>();
        services.AddScoped<MonthlyClosedService>();
        services.AddScoped<MonthlyClosingService>();
        services.AddScoped<MonthlyClosingReleaseService>();
        services.AddScoped<MonthlyClosingQueryService>();
        services.AddScoped<BillingReleaseService>();
        services.AddScoped<BillingClosedDateService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<DetailInvoiceService>();
        services.AddScoped<DetailInvoiceQueryService>();
        services.AddScoped<SettlementService>();
        services.AddScoped<ReceiptEntryService>();
        services.AddScoped<ReceiptQueryService>();
        services.AddScoped<DetailReceiptEntryService>();
        services.AddScoped<DetailReceiptQueryService>();
        services.AddScoped<CustomerLedgerQueryService>();
        services.AddScoped<DeliveryNoteService>();

        return services;
    }
}
