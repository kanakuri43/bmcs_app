using System.Windows.Controls;
using bmcs_app.ViewModels.Analysis;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Analysis;

/// <summary>得意先別・商品別売上・粗利画面。タイトルと列見出しは軸（<see cref="SalesSummaryViewModel.Kind"/>）に合わせて設定する。</summary>
public partial class SalesSummaryWindow : MetroWindow
{
    public SalesSummaryWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is SalesSummaryViewModel viewModel)
            {
                Title = $"bmcs_app - {viewModel.Title}";
                var columns = ((GridView)ResultList.View).Columns;
                columns[0].Header = viewModel.CodeHeader;
                columns[1].Header = viewModel.NameHeader;
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
