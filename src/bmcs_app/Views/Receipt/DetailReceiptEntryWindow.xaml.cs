using bmcs_app.ViewModels.Receipt;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Receipt;

/// <summary>明細入金画面（TODO.md 7-4）。</summary>
public partial class DetailReceiptEntryWindow : MetroWindow
{
    public DetailReceiptEntryWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is DetailReceiptEntryViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
