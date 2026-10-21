using bmcs_app.Application.Master;
using CommunityToolkit.Mvvm.ComponentModel;

namespace bmcs_app.ViewModels.Master;

/// <summary>
/// 得意先／商品／単価・掛率マスタ一覧の1行。単価・掛率は文字列で受け（不正な入力で元の値が残らないようにするため）、
/// 登録時に検証する。変更の有無は、読込時の文字列との比較で判定する。
/// </summary>
public partial class CustomerProductSettingRow : ObservableObject
{
    private readonly string _loadedPriceText;

    private readonly string _loadedRateText;

    public CustomerProductSettingRow(CustomerProductSetting setting, string taxLabel)
    {
        ProductCode = setting.ProductCode;
        ProductName = setting.ProductName;
        CatalogPrice = setting.CatalogPrice;
        TaxLabel = taxLabel;
        PriceRowVersion = setting.PriceRowVersion;
        RateRowVersion = setting.RateRowVersion;

        _loadedPriceText = setting.UnitPrice?.ToString("0.####") ?? string.Empty;
        _loadedRateText = setting.Rate?.ToString() ?? string.Empty;
        PriceText = _loadedPriceText;
        RateText = _loadedRateText;
    }

    public string ProductCode { get; }

    public string ProductName { get; }

    /// <summary>商品マスタの定価（税抜）。参照用で、定価が0なら掛率を登録しても単価は0になる。</summary>
    public decimal CatalogPrice { get; }

    /// <summary>単価の入力基準。得意先が内税明細単位なら「税込」、それ以外は「税抜」。</summary>
    public string TaxLabel { get; }

    public byte[]? PriceRowVersion { get; }

    public byte[]? RateRowVersion { get; }

    [ObservableProperty]
    public partial string PriceText { get; set; }

    [ObservableProperty]
    public partial string RateText { get; set; }

    public bool IsPriceChanged => PriceText.Trim() != _loadedPriceText;

    public bool IsRateChanged => RateText.Trim() != _loadedRateText;

    public bool IsChanged => IsPriceChanged || IsRateChanged;
}
