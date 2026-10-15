using bmcs_app.ViewModels.Common;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Common;

/// <summary>締め対象得意先の複数選択モーダル。</summary>
public partial class ClosingCustomerSelectDialog : MetroWindow
{
    public ClosingCustomerSelectDialog()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is ClosingCustomerSelectDialogViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
