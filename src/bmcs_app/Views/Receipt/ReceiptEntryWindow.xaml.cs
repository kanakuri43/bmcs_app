using bmcs_app.ViewModels.Receipt;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Receipt;

/// <summary>入金入力画面（TODO.md 7-2）。</summary>
public partial class ReceiptEntryWindow : MetroWindow
{
    public ReceiptEntryWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is ReceiptEntryViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
