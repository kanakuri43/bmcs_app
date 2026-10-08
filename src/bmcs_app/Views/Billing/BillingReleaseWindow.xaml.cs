using bmcs_app.ViewModels.Billing;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Billing;

/// <summary>締め解除処理画面。</summary>
public partial class BillingReleaseWindow : MetroWindow
{
    public BillingReleaseWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is BillingReleaseViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
