using bmcs_app.ViewModels.Closing;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Closing;

/// <summary>月次締め解除処理画面（TODO.md 9-3）。</summary>
public partial class MonthlyClosingReleaseWindow : MetroWindow
{
    public MonthlyClosingReleaseWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is MonthlyClosingReleaseViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
