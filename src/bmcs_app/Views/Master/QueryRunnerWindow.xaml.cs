using System.Windows.Controls;
using bmcs_app.ViewModels.Master;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Master;

/// <summary>クエリ実行画面。</summary>
public partial class QueryRunnerWindow : MetroWindow
{
    public QueryRunnerWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is QueryRunnerViewModel viewModel)
            {
                viewModel.LoadCommand.Execute(null);
            }
        };
    }

    // 列見出しの "_" がアクセスキー扱いで消えないよう、エスケープする。
    private void ResultGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e) =>
        e.Column.Header = e.Column.Header?.ToString()?.Replace("_", "__");
}
