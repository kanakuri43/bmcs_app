using System.Windows;
using bmcs_app.ViewModels.Master;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Master;

/// <summary>商品マスタExcel取込画面。</summary>
public partial class ProductExcelImportWindow : MetroWindow
{
    public ProductExcelImportWindow()
    {
        InitializeComponent();
    }

    private static string? DroppedXlsx(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files
        && files[0].EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? files[0]
            : null;

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedXlsx(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (DroppedXlsx(e) is { } path && DataContext is ProductExcelImportViewModel viewModel)
        {
            await viewModel.LoadFileAsync(path);
        }
    }
}
