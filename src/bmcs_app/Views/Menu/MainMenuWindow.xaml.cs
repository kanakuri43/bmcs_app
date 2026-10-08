using System.Windows;
using bmcs_app.ViewModels.Menu;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Menu;

/// <summary>メインメニュー画面。</summary>
public partial class MainMenuWindow : MetroWindow
{
    public MainMenuWindow()
    {
        InitializeComponent();

        // タスクバーを除いたメインモニタの作業領域いっぱいの高さにする（Left/Top は XAML で 0,0 に固定済み）。
        Height = SystemParameters.WorkArea.Height;

        // DataContext は WindowService が設定するため、Loaded で初期表示処理を起動する。
        Loaded += async (_, _) =>
        {
            if (DataContext is MainMenuViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
