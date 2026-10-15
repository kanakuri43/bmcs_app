using bmcs_app.ViewModels.Closing;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Closing;

/// <summary>月次締め確定ダイアログ。</summary>
public partial class MonthlyClosingConfirmDialog : MetroWindow
{
    public MonthlyClosingConfirmDialog()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is MonthlyClosingConfirmDialogViewModel viewModel)
            {
                viewModel.LoadCommand.Execute(null);
            }
        };
    }
}
