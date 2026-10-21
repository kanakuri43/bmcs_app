using bmcs_app.ViewModels.Master;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Master;

/// <summary>得意先／商品／単価・掛率マスタ画面。</summary>
public partial class CustomerProductPriceMasterWindow : MetroWindow
{
    public CustomerProductPriceMasterWindow()
    {
        InitializeComponent();

        // 登録していない変更があれば、閉じる前に破棄してよいか確認する。
        Closing += (_, e) =>
        {
            if (DataContext is CustomerProductPriceMasterViewModel viewModel && !viewModel.ConfirmDiscardChanges())
            {
                e.Cancel = true;
            }
        };
    }
}
