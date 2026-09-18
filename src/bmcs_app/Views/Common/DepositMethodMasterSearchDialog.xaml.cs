using bmcs_app.ViewModels.Common;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Common;

/// <summary>入金方法マスタ画面向けの入金方法検索モーダル。</summary>
public partial class DepositMethodMasterSearchDialog : MetroWindow
{
    public DepositMethodMasterSearchDialog()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is DepositMethodMasterSearchDialogViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }

            SearchKeywordTextBox.Focus();
        };
    }
}
