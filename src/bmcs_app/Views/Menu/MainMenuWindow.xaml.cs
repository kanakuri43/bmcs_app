using System.Windows;
using bmcs_app.ViewModels.Menu;

namespace bmcs_app.Views.Menu;

/// <summary>
/// メインメニュー画面。Phase 0-3 時点では共通基盤の動作確認を行う。
/// </summary>
public partial class MainMenuWindow : Window
{
    public MainMenuWindow()
    {
        InitializeComponent();

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
