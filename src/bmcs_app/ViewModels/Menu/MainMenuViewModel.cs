using System.Collections.ObjectModel;
using System.Windows;
using bmcs_app.Application.Common;
using bmcs_app.Application.Master;
using bmcs_app.Domain.Calculations;
using bmcs_app.Services;
using bmcs_app.ViewModels.Billing;
using bmcs_app.ViewModels.Closing;
using bmcs_app.ViewModels.Ledger;
using bmcs_app.ViewModels.Master;
using bmcs_app.ViewModels.Order;
using bmcs_app.ViewModels.Receipt;
using bmcs_app.ViewModels.Sales;
using bmcs_app.Views.Billing;
using bmcs_app.Views.Closing;
using bmcs_app.Views.Ledger;
using bmcs_app.Views.Master;
using bmcs_app.Views.Order;
using bmcs_app.Views.Receipt;
using bmcs_app.Views.Sales;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Menu;

/// <summary>
/// メインメニュー画面。メニュー構成マスタ（`menu`）を親子階層で読み込み、
/// 現在の社員の権限レベルで絞り込んだ結果を表示する（デザインモックのC案＝リスト・アコーディオン型）。
/// メニュー項目自体の追加・編集画面は持たない（DBへ直接投入する運用。`scripts/014_seed_menu_structure.sql`）。
/// </summary>
public partial class MainMenuViewModel(
    MenuService menuService,
    EmployeeService employeeService,
    ICurrentEmployeeContext currentEmployeeContext,
    DatabaseConnectionInfo databaseConnectionInfo,
    WindowService windowService) : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmployeeInitial))]
    public partial string EmployeeName { get; set; } = string.Empty;

    /// <summary>ヘッダーのアバターに表示する社員名の先頭1文字。</summary>
    public string EmployeeInitial => EmployeeName.Length > 0 ? EmployeeName[..1] : string.Empty;

    [ObservableProperty]
    public partial byte PermissionLevel { get; set; }

    /// <summary>フッターに常時表示する接続先情報（サーバー名・DB名）。</summary>
    public string ConnectionInfo { get; } =
        $"接続先: {databaseConnectionInfo.ServerName} / {databaseConnectionInfo.DatabaseName}";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public ObservableCollection<MenuCategoryDisplayItem> Categories { get; } = [];

    /// <summary>画面表示時に社員の権限レベルとメニュー構成を読み込む（Window の Loaded から呼ばれる）。</summary>
    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var employee = await employeeService.GetByCodeAsync(employeeCode);
        if (employee is null)
        {
            EmployeeName = employeeCode;
            PermissionLevel = 0;
            StatusMessage = $"社員コード「{employeeCode}」が見つかりません。権限レベル0として扱います。";
        }
        else
        {
            EmployeeName = employee.EmployeeName;
            PermissionLevel = employee.PermissionLevel;
            StatusMessage = string.Empty;
        }

        var menuRows = await menuService.GetMenuTreeAsync();
        var tree = MenuTreeBuilder.Build(menuRows, PermissionLevel);

        Categories.Clear();
        for (var i = 0; i < tree.Count; i++)
        {
            var category = tree[i];
            var items = category.Items
                .Select(item => new MenuItemDisplayItem(item.MenuName, item.ScreenKey, i))
                .ToList();
            Categories.Add(new MenuCategoryDisplayItem(category.MenuName, i, items, category.IsDefaultExpanded));
        }
    });

    /// <summary>メニュー項目（リーフ）を選択したときに対応する画面を開く。</summary>
    [RelayCommand]
    private void OpenMenuItem(string? screenKey)
    {
        switch (screenKey)
        {
            case "order_entry":
                windowService.Show<OrderEntryWindow, OrderEntryViewModel>();
                break;
            case "sales_entry":
                windowService.Show<SalesEntryWindow, SalesEntryViewModel>();
                break;
            case "copier_csv_import":
                windowService.Show<CopierSalesImportWindow, CopierSalesImportViewModel>();
                break;
            case "billing_closing":
                windowService.Show<BillingClosingWindow, BillingClosingViewModel>();
                break;
            case "monthly_closing":
                windowService.Show<MonthlyClosingWindow, MonthlyClosingViewModel>();
                break;
            case "monthly_release":
                windowService.Show<MonthlyClosingReleaseWindow, MonthlyClosingReleaseViewModel>();
                break;
            case "billing_release":
                windowService.Show<BillingReleaseWindow, BillingReleaseViewModel>();
                break;
            case "detail_invoice_issue":
                windowService.Show<DetailInvoiceIssueWindow, DetailInvoiceIssueViewModel>();
                break;
            case "receipt_entry":
                windowService.Show<ReceiptEntryWindow, ReceiptEntryViewModel>();
                break;
            case "detail_receipt_entry":
                windowService.Show<DetailReceiptEntryWindow, DetailReceiptEntryViewModel>();
                break;
            case "customer_ledger":
                windowService.Show<CustomerLedgerWindow, CustomerLedgerViewModel>();
                break;
            case "customer_master":
                windowService.Show<CustomerMasterWindow, CustomerMasterViewModel>();
                break;
            case "product_master":
                windowService.Show<ProductMasterWindow, ProductMasterViewModel>();
                break;
            case "employee_master":
                windowService.Show<EmployeeMasterWindow, EmployeeMasterViewModel>();
                break;
            case "company_info_settings":
                windowService.Show<CompanyInfoSettingsWindow, CompanyInfoSettingsViewModel>();
                break;
            case "menu_master":
                windowService.Show<MenuMasterWindow, MenuMasterViewModel>();
                break;
            case "bank_account_master":
                windowService.Show<BankAccountMasterWindow, BankAccountMasterViewModel>();
                break;
            case "copier_machine_master":
                windowService.Show<CopierMachineMasterWindow, CopierMachineMasterViewModel>();
                break;
            case "deposit_method_master":
                windowService.Show<DepositMethodMasterWindow, DepositMethodMasterViewModel>();
                break;
            case "printer_settings":
                windowService.Show<PrinterSettingsWindow, PrinterSettingsViewModel>();
                break;
            default:
                MessageBox.Show($"画面「{screenKey}」は未実装です。", "bmcs_app", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
        }
    }
}

/// <summary>メニューのカテゴリ（親）の表示用。<see cref="ColorIndex"/> は表示順に応じた色分け用インデックス。</summary>
public sealed record MenuCategoryDisplayItem(
    string MenuName, int ColorIndex, IReadOnlyList<MenuItemDisplayItem> Items, bool IsExpanded);

/// <summary>メニュー項目（子・リーフ）の表示用。</summary>
public sealed record MenuItemDisplayItem(string MenuName, string ScreenKey, int ColorIndex);
