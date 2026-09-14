using bmcs_app.ViewModels.Common;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Common;

/// <summary>銀行マスタ画面向けの銀行口座検索モーダル。</summary>
public partial class BankAccountMasterSearchDialog : MetroWindow
{
    public BankAccountMasterSearchDialog()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is BankAccountMasterSearchDialogViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }

            SearchKeywordTextBox.Focus();
        };
    }
}
