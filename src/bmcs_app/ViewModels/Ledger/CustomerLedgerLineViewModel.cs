using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.ViewModels.Ledger;

/// <summary>
/// 得意先元帳グリッドの1行（表示専用、TODO.md 8-1）。金額・日付・数量は <see cref="Entry"/> の
/// プロパティを XAML の <c>StringFormat</c> で直接描画させる（null は空欄になる。専用コンバーターは
/// 作らない既存慣行と同じ）。本クラスは区分・商品名欄など、複数のプロパティを組み合わせないと
/// 表せない日本語ラベルだけを組み立てる。
/// </summary>
/// <param name="entry">元帳の1行。</param>
/// <param name="isTransactionHistoryOnly">
/// 請求集約元の取引履歴のみモード（TODO.md 12-E）。true のとき <see cref="BalanceDisplay"/> を
/// 空欄にする（残高・繰越を表示しない。docs/design_document.md 28-2節 #6）。
/// </param>
public sealed class CustomerLedgerLineViewModel(CustomerLedgerEntry entry, bool isTransactionHistoryOnly = false)
{
    public CustomerLedgerEntry Entry { get; } = entry;

    /// <summary>
    /// 得意先欄。請求集約先の元帳ではグループ内の複数得意先の伝票が混在するため、
    /// どの得意先の伝票かを表示する（売上・入金行以外は空欄）。
    /// </summary>
    public string CustomerLabel => Entry.CustomerCode is { } code ? $"{code} {Entry.CustomerName}" : string.Empty;

    /// <summary>
    /// 残高欄。取引履歴のみモード（<see cref="Entry"/> の残高は常に 0）では表示しない
    /// （<see cref="Entry.Balance"/> は非nullableのため、専用コンバーターを作らない既存慣行に
    /// 従い ViewModel 側で nullable にする）。
    /// </summary>
    public decimal? BalanceDisplay => isTransactionHistoryOnly ? null : Entry.Balance;

    /// <summary>
    /// 区分。売上系（<see cref="LedgerEntryKind.Sales"/>）は <see cref="Domain.Entities.Sales.SlipType"/>
    /// を優先する（売上／返品／値引）。証跡の継続行（TODO.md 8-1 D-3）は SlipType が null のため空欄になる。
    /// </summary>
    public string KindLabel => Entry.Kind switch
    {
        LedgerEntryKind.OpeningBalance => "前月繰越",
        LedgerEntryKind.Sales => Entry.SlipType switch
        {
            SlipType.Sales => "売上",
            SlipType.Return => "返品",
            SlipType.Discount => "値引",
            _ => string.Empty,
        },
        LedgerEntryKind.ConsumptionTax => "消費税",
        LedgerEntryKind.Receipt => "入金",
        _ => string.Empty,
    };

    /// <summary>商品名欄。消費税行・入金の独立行は説明文に差し替える。</summary>
    public string Description => Entry.Kind switch
    {
        LedgerEntryKind.Sales => Entry.ProductName ?? string.Empty,
        LedgerEntryKind.ConsumptionTax => Entry.BillingNumber is { } billingNumber
            ? $"消費税（請求No. {billingNumber}）"
            : Entry.SalesSlipNumber is not null
                ? "消費税（伝票単位）"
                : "消費税（未締め分・仮計算）",
        LedgerEntryKind.Receipt => Entry.DepositMethodName is { } name ? $"入金（{name}）" : "入金",
        _ => string.Empty,
    };

    /// <summary>請求済みマーク（＊）。デモ画面の「請求」列を踏襲。</summary>
    public string BilledMark => Entry.BillingStatus == BillingLinkStatus.Billed ? "＊" : string.Empty;
}
