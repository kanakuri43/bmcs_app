using bmcs_app.ViewModels.Master;
using MahApps.Metro.Controls;

namespace bmcs_app.Views.Master;

/// <summary>仕入先／商品／原価マスタ画面。</summary>
public partial class ProductSupplierCostMasterWindow : MetroWindow
{
    public ProductSupplierCostMasterWindow()
    {
        InitializeComponent();

        // 登録していない変更があれば、閉じる前に破棄してよいか確認する。
        Closing += (_, e) =>
        {
            if (DataContext is ProductSupplierCostMasterViewModel viewModel && !viewModel.ConfirmDiscardChanges())
            {
                e.Cancel = true;
            }
        };
    }
}
