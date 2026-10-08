using System.Windows.Input;
using bmcs_app.ViewModels.Receipt;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Receipt;

/// <summary>入金入力画面。</summary>
public partial class ReceiptEntryWindow : MetroWindow
{
    public ReceiptEntryWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is ReceiptEntryViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };

        // 伝票プレビューには保存・取消以外の退出手段が無いため、Escで閉じられるようにする。
        // 通常の編集セッションでは無効（未保存の入力をEscで誤って破棄しない）。
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is ReceiptEntryViewModel { IsPreviewMode: true })
            {
                Close();
            }
        };
    }
}
