using System.Windows.Controls;
using bmcs_app.ViewModels.Analysis;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Analysis;

/// <summary>売上・粗利分析画面（担当者別・得意先別・商品別のタブ）。列見出しは選択中の軸（<see cref="SalesSummaryViewModel.Kind"/>）に合わせて設定する。</summary>
public partial class SalesSummaryWindow : MetroWindow
{
    public SalesSummaryWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is SalesSummaryViewModel viewModel)
            {
                SetHeaders(viewModel);
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SalesSummaryViewModel.SelectedTabIndex))
                    {
                        SetHeaders(viewModel);
                    }
                };
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }

    private void SetHeaders(SalesSummaryViewModel viewModel)
    {
        var columns = ((GridView)ResultList.View).Columns;
        columns[0].Header = viewModel.CodeHeader;
        columns[1].Header = viewModel.NameHeader;
    }
}
