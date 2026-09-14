using bmcs_app.ViewModels.Menu;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Menu;

/// <summary>メインメニュー画面（TODO.md 2-7）。</summary>
public partial class MainMenuWindow : MetroWindow
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
