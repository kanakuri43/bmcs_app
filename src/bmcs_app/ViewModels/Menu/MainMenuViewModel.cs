using bmcs_app.Application.Common;
using bmcs_app.Services;
using bmcs_app.ViewModels.Master;
using bmcs_app.Views.Master;
using bmcs_app.Views.Menu;
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
    /// 得意先マスタ画面を開く。Phase 2-7 で正式なメニューに置き換わるまでの暫定導線。
    /// </summary>
    [RelayCommand]
    private void OpenCustomerMaster()
        => windowService.Show<CustomerMasterWindow, CustomerMasterViewModel>();

    /// <summary>
    /// プリンタ設定画面を開く。Phase 2-7 で正式なメニューに置き換わるまでの暫定導線。
    /// </summary>
    [RelayCommand]
    private void OpenPrinterSettings()
        => windowService.Show<PrinterSettingsWindow, PrinterSettingsViewModel>();
}
