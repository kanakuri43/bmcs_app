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
}
