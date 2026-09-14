using bmcs_app.ViewModels.Master;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Master;

/// <summary>自社情報マスタ画面。</summary>
public partial class CompanyInfoSettingsWindow : MetroWindow
{
    public CompanyInfoSettingsWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is CompanyInfoSettingsViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
