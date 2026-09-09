using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Common;

/// <summary>
/// 伝票明細行の1行分（TODO.md 4-2）。受注入力・売上入力（TODO.md 5-2）で共用する
/// 明細行 ViewModel。行ごとの UserControl（<c>Views/Common/SlipLineControl.xaml</c>）と対で使う。
/// </summary>
public partial class SlipLineViewModel : ObservableObject
{
    private readonly Func<SlipLineViewModel, Task> _onOpenProductLookup;
    private readonly Func<SlipLineViewModel, Task> _onLookupProductByCode;
    private readonly Action<SlipLineViewModel> _onDelete;

    /// <summary>商品確定後に数量欄へフォーカスを移動するよう要求するイベント（View コードビハインドがハンドル）。</summary>
    public event Action? MoveToQuantityRequested;

    public SlipLineViewModel(
        Func<SlipLineViewModel, Task> onOpenProductLookup,
        Func<SlipLineViewModel, Task> onLookupProductByCode,
        Action<SlipLineViewModel> onDelete)
    {
        _onOpenProductLookup = onOpenProductLookup;
        _onLookupProductByCode = onLookupProductByCode;
        _onDelete = onDelete;
    }

    public void RequestMoveToQuantity() => MoveToQuantityRequested?.Invoke();

    [ObservableProperty]
    public partial short LineNumber { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TaxRateDisplay))]
    public partial string ProductCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProductName { get; set; } = string.Empty;

    /// <summary>諸口等の手入力商品を将来許す余地として残すが、当面は常に読み取り専用（4-3で確定）。</summary>
    [ObservableProperty]
    public partial bool IsProductNameReadOnly { get; set; } = true;

    [ObservableProperty]
    public partial string? Specification { get; set; }

    [ObservableProperty]
    public partial string? UnitName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Amount))]
    public partial decimal Quantity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Amount))]
    public partial decimal UnitPrice { get; set; }

    /// <summary>粗利計算用。商品マスタの標準原価を転記する（表示のみ、上書き不可）。</summary>
    [ObservableProperty]
    public partial decimal CostPrice { get; set; }

    [ObservableProperty]
    public partial TaxCategory TaxCategory { get; set; }

    /// <summary>税率のスナップショット（%）。伝票日付時点の税率マスタから解決した値。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TaxRateDisplay))]
    public partial decimal TaxRate { get; set; }

    /// <summary>表示用税率文字列。商品未選択の空行では税率0を「—」と表示する。</summary>
    public string TaxRateDisplay => IsBlank ? "—" : $"{TaxRate:0.##}%";

    /// <summary>行摘要。</summary>
    [ObservableProperty]
    public partial string LineRemarks { get; set; } = string.Empty;

    /// <summary>
    /// 得意先の端数区分。得意先はあとから差し替わりうるため、ホスト側が変更時に全行へ設定し直し、
    /// <see cref="Amount"/> を再通知する（コンストラクタ注入では対応できない）。
    /// 得意先未選択の空行でも <see cref="Amount"/> の評価（バインディング）が例外にならないよう、
    /// 既定値として有効な区分を1つ持たせる。
    /// </summary>
    public RoundingType RoundingType { get; set; } = RoundingType.Floor;

    /// <summary>数量×単価を得意先の端数区分で1円に丸めた金額。TaxUnit=Line の得意先は税込金額になる。</summary>
    public decimal Amount => ConsumptionTaxCalculator.CalculateLineAmount(Quantity, UnitPrice, RoundingType);

    public void RaiseAmountChanged() => OnPropertyChanged(nameof(Amount));

    public bool IsBlank => string.IsNullOrWhiteSpace(ProductCode);

    [RelayCommand]
    private Task OpenProductLookupAsync() => _onOpenProductLookup(this);

    [RelayCommand]
    private Task LookupProductByCodeAsync() => _onLookupProductByCode(this);

    [RelayCommand]
    private void Delete() => _onDelete(this);
}
