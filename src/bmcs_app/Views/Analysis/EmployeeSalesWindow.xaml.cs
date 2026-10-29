using bmcs_app.ViewModels.Analysis;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Analysis;

/// <summary>担当者別売上・粗利画面。</summary>
public partial class EmployeeSalesWindow : MetroWindow
{
    public EmployeeSalesWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is EmployeeSalesViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }
}
