using bmcs_app.ViewModels.Common;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Common;

/// <summary>伝票検索モーダル。</summary>
public partial class SlipSearchDialog : MetroWindow
{
    public SlipSearchDialog()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is SlipSearchDialogViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }

            SearchKeywordTextBox.Focus();
        };
    }
}
