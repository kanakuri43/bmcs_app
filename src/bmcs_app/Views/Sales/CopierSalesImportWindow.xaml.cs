using System.Windows;
using bmcs_app.ViewModels.Sales;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Sales;

/// <summary>コピー機売上CSV取込画面。</summary>
public partial class CopierSalesImportWindow : MetroWindow
{
    public CopierSalesImportWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is CopierSalesImportViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };
    }

    private static string? DroppedCsv(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files
        && files[0].EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? files[0]
            : null;

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedCsv(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (DroppedCsv(e) is { } path && DataContext is CopierSalesImportViewModel viewModel)
        {
            await viewModel.LoadFileAsync(path);
        }
    }
}
