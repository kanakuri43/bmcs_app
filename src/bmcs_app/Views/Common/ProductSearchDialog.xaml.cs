using System.Windows.Controls;
using bmcs_app.ViewModels.Common;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Common;

/// <summary>商品検索モーダル（TODO.md 3-2）。</summary>
public partial class ProductSearchDialog : MetroWindow
{
    public ProductSearchDialog()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is ProductSearchDialogViewModel viewModel)
            {
                await viewModel.LoadMasterCommand.ExecuteAsync(null);
            }

            MasterSearchKeywordTextBox.Focus();
        };
    }

    /// <summary>履歴タブが最初に選択されたときに1回だけ問い合わせる。</summary>
    private async void OnSearchTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, SearchTabControl))
        {
            return;
        }

        if (SearchTabControl.SelectedItem == HistoryTabItem
            && DataContext is ProductSearchDialogViewModel viewModel)
        {
            await viewModel.LoadHistoryCommand.ExecuteAsync(null);
            HistorySearchKeywordTextBox.Focus();
        }
    }
}
