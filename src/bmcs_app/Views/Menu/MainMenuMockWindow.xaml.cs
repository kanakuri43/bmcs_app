using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Menu;

/// <summary>
/// Phase 2-7 のデザイン検討用モック。DI・実データとは連動しない見た目確認専用。
/// </summary>
public partial class MainMenuMockWindow : MetroWindow
{
    public MainMenuMockWindow()
    {
        InitializeComponent();
    }

    private void MenuTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string label })
        {
            LastClickedText.Text = $"選択: {label}（モックのため画面遷移はしません）";
        }
    }
}
