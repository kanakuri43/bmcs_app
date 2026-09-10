using bmcs_app.ViewModels.Sales;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Sales;

/// <summary>売上入力画面（TODO.md 5-2）。</summary>
public partial class SalesEntryWindow : MetroWindow
{
    public SalesEntryWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is SalesEntryViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
