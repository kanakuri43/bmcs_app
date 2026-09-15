using bmcs_app.ViewModels.Common;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Common;

/// <summary>帳票プレビューダイアログ（TODO.md 10-3、帳票基盤）。</summary>
public partial class ReportPreviewDialog : MetroWindow
{
    public ReportPreviewDialog()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is ReportPreviewDialogViewModel viewModel)
            {
                viewModel.LoadCommand.Execute(null);
            }
        };
    }
}
