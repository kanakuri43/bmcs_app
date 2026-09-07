using bmcs_app.ViewModels.Master;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Master;

/// <summary>商品マスタ画面。</summary>
public partial class ProductMasterWindow : MetroWindow
{
    public ProductMasterWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is ProductMasterViewModel viewModel)
            {
                await viewModel.LoadProductsCommand.ExecuteAsync(null);
            }
        };
    }
}
