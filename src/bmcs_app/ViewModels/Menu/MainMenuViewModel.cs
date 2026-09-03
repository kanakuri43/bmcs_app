using bmcs_app.Application.Common;
using bmcs_app.Services;
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
}
