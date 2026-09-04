using bmcs_app.ViewModels.Master;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Master;

/// <summary>得意先マスタ画面。</summary>
public partial class CustomerMasterWindow : MetroWindow
{
    public CustomerMasterWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is CustomerMasterViewModel viewModel)
            {
                await viewModel.LoadCustomersCommand.ExecuteAsync(null);
            }
        };
    }
}
