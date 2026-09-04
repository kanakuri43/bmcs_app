using bmcs_app.ViewModels.Master;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Master;

/// <summary>プリンタ環境設定画面。</summary>
public partial class PrinterSettingsWindow : MetroWindow
{
    public PrinterSettingsWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is PrinterSettingsViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
