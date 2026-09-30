using bmcs_app.ViewModels.Closing;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Closing;

/// <summary>月次締め処理画面（TODO.md 9-1）。</summary>
public partial class MonthlyClosingWindow : MetroWindow
{
    public MonthlyClosingWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is MonthlyClosingViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
