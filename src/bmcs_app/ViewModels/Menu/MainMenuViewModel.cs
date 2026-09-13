using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Services;
using bmcs_app.ViewModels.Billing;
using bmcs_app.ViewModels.Common;
using bmcs_app.ViewModels.Master;
using bmcs_app.ViewModels.Order;
using bmcs_app.ViewModels.Sales;
using bmcs_app.Views.Billing;
using bmcs_app.Views.Common;
using bmcs_app.Views.Master;
using bmcs_app.Views.Menu;
using bmcs_app.Views.Order;
using bmcs_app.Views.Sales;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Menu;

/// <summary>
/// Phase 0-3 時点の動作確認用 ViewModel。
/// 権限に応じたメニューの出し分けは Phase 2-7 で実装する。
/// </summary>
public partial class MainMenuViewModel(
    DatabaseHealthService databaseHealthService,
    WindowService windowService) : ViewModelBase
{
    [ObservableProperty]
    public partial string ConnectionInfo { get; set; } = "未接続";

    [ObservableProperty]
    public partial string ServerVersion { get; set; } = string.Empty;

    /// <summary>
    /// Phase 0-5 書式確認用のサンプル値（日付）。
    /// DateOnly には既定の TypeConverter がなく TextBox.Text との双方向バインディングに
    /// 使えないため、文字列で保持する（入力の形式チェックは ViewModel の責務。architecture.md 11章）。
    /// </summary>
    [ObservableProperty]
    public partial string SampleDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy/MM/dd");

    /// <summary>Phase 0-5 書式確認用のサンプル値（金額）。</summary>
    [ObservableProperty]
    public partial decimal SampleAmount { get; set; } = 1234567m;

    /// <summary>Phase 0-5 書式確認用のサンプル値（数量）。</summary>
    [ObservableProperty]
    public partial decimal SampleQuantity { get; set; } = 42m;

    /// <summary>Phase 0-6 キーボード操作確認用のサンプル一覧。</summary>
    public IReadOnlyList<string> SampleItems { get; } = ["得意先A", "得意先B", "得意先C"];

    /// <summary>Phase 0-6 キーボード操作確認用: Enter で確定・転記した結果の表示先。</summary>
    [ObservableProperty]
    public partial string TranscribedItem { get; set; } = string.Empty;

    /// <summary>選択中の行を確定・転記する（RowActivationBehavior から Enter で呼ばれる）。</summary>
    [RelayCommand]
    private void TranscribeItem(object? item) => TranscribedItem = item as string ?? string.Empty;

    /// <summary>
    /// このウィンドウのスコープが使っている DbContext の識別子。
    /// ウィンドウを複数開いたとき、値が異なればスコープが分離できている。
    /// </summary>
    public string DbContextInstanceId => databaseHealthService.DbContextInstanceId.ToString();

    /// <summary>画面表示時に接続先の情報を非同期で取得する。</summary>
    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(async () =>
    {
        var info = await databaseHealthService.GetDatabaseInfoAsync();
        ConnectionInfo = $"{info.DataSource} / {info.Database}";
        ServerVersion = info.ServerVersion;
    });

    /// <summary>もう1つウィンドウを開く。スコープが分離されることの確認用。</summary>
    [RelayCommand]
    private void OpenAnotherWindow()
        => windowService.Show<MainMenuWindow, MainMenuViewModel>();

    /// <summary>
    /// Phase 2-7 のデザイン検討用モックを開く。DI登録していない単純な Window のため
    /// WindowService は使わず直接生成する（見た目確認専用、実データとは連動しない）。
    /// </summary>
    [RelayCommand]
    private void OpenMainMenuMock()
        => new MainMenuMockWindow().Show();

    /// <summary>
    /// 得意先マスタ画面を開く。Phase 2-7 で正式なメニューに置き換わるまでの暫定導線。
    /// </summary>
    [RelayCommand]
    private void OpenCustomerMaster()
        => windowService.Show<CustomerMasterWindow, CustomerMasterViewModel>();

    /// <summary>
    /// 商品マスタ画面を開く。Phase 2-7 で正式なメニューに置き換わるまでの暫定導線。
    /// </summary>
    [RelayCommand]
    private void OpenProductMaster()
        => windowService.Show<ProductMasterWindow, ProductMasterViewModel>();

    /// <summary>
    /// プリンタ設定画面を開く。Phase 2-7 で正式なメニューに置き換わるまでの暫定導線。
    /// </summary>
    [RelayCommand]
    private void OpenPrinterSettings()
        => windowService.Show<PrinterSettingsWindow, PrinterSettingsViewModel>();

    private Customer? _selectedCustomer;

    /// <summary>選択済みの得意先（商品検索モーダルの履歴軸に渡す）。TODO.md 3-1/3-2 の動作確認用。</summary>
    [ObservableProperty]
    public partial string SelectedCustomerCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedCustomerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TranscribedProducts { get; set; } = string.Empty;

    /// <summary>
    /// 得意先検索モーダルを開く（TODO.md 3-1）。Phase 4/5 の伝票入力画面が実装されるまでの動作確認用導線。
    /// </summary>
    [RelayCommand]
    private void OpenCustomerSearch()
    {
        var customer = windowService.ShowDialog<CustomerSearchDialog, CustomerSearchDialogViewModel, Customer>();
        if (customer is null)
        {
            return;
        }

        _selectedCustomer = customer;
        SelectedCustomerCode = customer.CustomerCode;
        SelectedCustomerName = customer.CustomerName;
    }

    /// <summary>
    /// 商品検索モーダルを開く（TODO.md 3-2）。Phase 4/5 の伝票入力画面が実装されるまでの動作確認用導線。
    /// 得意先の税区分（TargetTaxUnit）を渡すことで、単価列の外税／内税判定を実機確認できる（TODO.md 4-2）。
    /// </summary>
    [RelayCommand]
    private void OpenProductSearch()
    {
        var customerCode = SelectedCustomerCode;
        var taxUnit = _selectedCustomer?.TaxUnit;
        var selections = windowService.ShowDialog<ProductSearchDialog, ProductSearchDialogViewModel, IReadOnlyList<ProductSelection>>(
            vm =>
            {
                vm.TargetCustomerCode = string.IsNullOrWhiteSpace(customerCode) ? null : customerCode;
                vm.TargetTaxUnit = taxUnit;
            });

        TranscribedProducts = selections is null || selections.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, selections.Select(s => $"{s.ProductCode} {s.ProductName} @{s.UnitPrice:N0}（{s.Source}）"));
    }

    /// <summary>
    /// 受注入力画面を開く（TODO.md 4-2）。Phase 2-7 で正式なメニューに置き換わるまでの暫定導線。
    /// </summary>
    [RelayCommand]
    private void OpenOrderEntry()
        => windowService.Show<OrderEntryWindow, OrderEntryViewModel>();

    /// <summary>
    /// 売上入力画面を開く（TODO.md 5-2）。Phase 2-7 で正式なメニューに置き換わるまでの暫定導線。
    /// </summary>
    [RelayCommand]
    private void OpenSalesEntry()
        => windowService.Show<SalesEntryWindow, SalesEntryViewModel>();

    /// <summary>
    /// 請求締め処理画面を開く（TODO.md 6-1）。Phase 2-7 で正式なメニューに置き換わるまでの暫定導線。
    /// </summary>
    [RelayCommand]
    private void OpenBillingClosing()
        => windowService.Show<BillingClosingWindow, BillingClosingViewModel>();

    /// <summary>
    /// 締め解除処理画面を開く（TODO.md 6-2）。管理者権限のみの操作だが、権限判定基盤（0-7）が
    /// 未着手のため現時点では制限しない。請求締め処理とは別画面（C-8・2026-09-10確定）。
    /// Phase 2-7 で正式なメニューに置き換わるまでの暫定導線。
    /// </summary>
    [RelayCommand]
    private void OpenBillingRelease()
        => windowService.Show<BillingReleaseWindow, BillingReleaseViewModel>();
}
