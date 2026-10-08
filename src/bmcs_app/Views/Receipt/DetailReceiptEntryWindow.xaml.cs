using System.Windows.Input;
using bmcs_app.ViewModels.Receipt;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Receipt;

/// <summary>明細入金画面。</summary>
public partial class DetailReceiptEntryWindow : MetroWindow
{
    public DetailReceiptEntryWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is DetailReceiptEntryViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };

        // 伝票プレビューには保存・取消以外の退出手段が無いため、Escで閉じられるようにする。
        // 通常の編集セッションでは無効（未保存の入力をEscで誤って破棄しない）。
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is DetailReceiptEntryViewModel { IsPreviewMode: true })
            {
                Close();
            }
        };
    }
}
