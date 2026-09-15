using System.Windows.Input;
using bmcs_app.ViewModels.Sales;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Sales;

/// <summary>売上入力画面（TODO.md 5-2）。</summary>
public partial class SalesEntryWindow : MetroWindow
{
    public SalesEntryWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is SalesEntryViewModel viewModel)
            {
                await viewModel.LoadCommand.ExecuteAsync(null);
            }
        };

        // 伝票プレビュー（TODO.md 8-3）には保存・取消以外の退出手段が無いため、Escで閉じられるようにする。
        // 通常の編集セッションでは無効（未保存の入力をEscで誤って破棄しない）。
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is SalesEntryViewModel { IsPreviewMode: true })
            {
                Close();
            }
        };
    }
}
