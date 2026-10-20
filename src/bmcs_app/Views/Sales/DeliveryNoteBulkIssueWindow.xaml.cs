using bmcs_app.ViewModels.Sales;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Sales;

/// <summary>納品書一括発行画面。</summary>
public partial class DeliveryNoteBulkIssueWindow : MetroWindow
{
    public DeliveryNoteBulkIssueWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is DeliveryNoteBulkIssueViewModel viewModel)
            {
                await viewModel.ReloadCommand.ExecuteAsync(null);
            }
        };
    }
}
