using bmcs_app.ViewModels.Sales;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Sales;

/// <summary>コピー機売上CSV取込画面。</summary>
public partial class CopierSalesImportWindow : MetroWindow
{
    public CopierSalesImportWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is CopierSalesImportViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
