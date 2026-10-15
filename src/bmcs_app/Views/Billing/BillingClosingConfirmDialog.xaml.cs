using bmcs_app.ViewModels.Billing;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Billing;

/// <summary>締確定ダイアログ。</summary>
public partial class BillingClosingConfirmDialog : MetroWindow
{
    public BillingClosingConfirmDialog()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is BillingClosingConfirmDialogViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
