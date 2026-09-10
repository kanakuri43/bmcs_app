using bmcs_app.ViewModels.Common;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Common;

/// <summary>伝票検索モーダル（TODO.md 5-3・5-5・5-6の共通前提）。</summary>
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
