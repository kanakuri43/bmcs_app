using System.Windows;
using bmcs_app.ViewModels.Billing;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Billing;

/// <summary>請求締め処理画面。</summary>
public partial class BillingClosingWindow : MetroWindow
{
    public BillingClosingWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is BillingClosingViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }

    // フォーカスを一覧へ戻す（非アクティブな選択表示は薄いグレーになり、選択されたか分かりにくいため）。
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        ResultsList.SelectAll();
        ResultsList.Focus();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        ResultsList.UnselectAll();
        ResultsList.Focus();
    }
}
