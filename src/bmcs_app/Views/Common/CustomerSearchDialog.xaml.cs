using bmcs_app.ViewModels.Common;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Common;

/// <summary>得意先検索モーダル（TODO.md 3-1）。</summary>
public partial class CustomerSearchDialog : MetroWindow
{
    public CustomerSearchDialog()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is CustomerSearchDialogViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }

            SearchKeywordTextBox.Focus();
        };
    }
}
