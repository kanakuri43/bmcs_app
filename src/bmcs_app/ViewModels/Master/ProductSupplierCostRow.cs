using bmcs_app.Application.Master;
using CommunityToolkit.Mvvm.ComponentModel;

namespace bmcs_app.ViewModels.Master;

/// <summary>
/// 仕入先／商品／原価マスタ一覧の1行。原価は文字列で受け（不正な入力で元の値が残らないようにするため）、
/// 登録時に検証する。変更の有無は、読込時の文字列との比較で判定する。
/// </summary>
public partial class ProductSupplierCostRow : ObservableObject
{
    private readonly string _loadedCostText;

    public ProductSupplierCostRow(ProductSupplierCostSetting setting)
    {
        ProductCode = setting.ProductCode;
        ProductName = setting.ProductName;
        StandardCostPrice = setting.StandardCostPrice;
        RowVersion = setting.RowVersion;

        _loadedCostText = setting.CostPrice?.ToString("0.####") ?? string.Empty;
        CostText = _loadedCostText;
    }

    public string ProductCode { get; }

    public string ProductName { get; }

    /// <summary>商品マスタの標準原価。参照用で、この仕入先の原価が空欄なら伝票明細にはこの値が入る。</summary>
    public decimal StandardCostPrice { get; }

    public byte[]? RowVersion { get; }

    [ObservableProperty]
    public partial string CostText { get; set; }

    public bool IsChanged => CostText.Trim() != _loadedCostText;
}
