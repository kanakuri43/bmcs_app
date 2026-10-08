using bmcs_app.ViewModels.Order;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Order;

/// <summary>受注入力画面。</summary>
public partial class OrderEntryWindow : MetroWindow
{
    public OrderEntryWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is OrderEntryViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
