using bmcs_app.ViewModels.Master;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Master;

/// <summary>消費税率マスタ画面。</summary>
public partial class TaxRateMasterWindow : MetroWindow
{
    public TaxRateMasterWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is TaxRateMasterViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
