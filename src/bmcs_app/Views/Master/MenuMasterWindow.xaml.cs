using bmcs_app.ViewModels.Master;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Master;

/// <summary>メニュー構成マスタ画面。</summary>
public partial class MenuMasterWindow : MetroWindow
{
    public MenuMasterWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is MenuMasterViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
