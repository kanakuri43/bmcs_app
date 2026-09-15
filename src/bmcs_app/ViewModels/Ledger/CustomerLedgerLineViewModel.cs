using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.ViewModels.Ledger;

/// <summary>
/// 得意先元帳グリッドの1行（表示専用、TODO.md 8-1）。金額・日付・数量は <see cref="Entry"/> の
/// プロパティを XAML の <c>StringFormat</c> で直接描画させる（null は空欄になる。専用コンバーターは
/// 作らない既存慣行と同じ）。本クラスは区分・商品名欄など、複数のプロパティを組み合わせないと
/// 表せない日本語ラベルだけを組み立てる。
/// </summary>
public sealed class CustomerLedgerLineViewModel(CustomerLedgerEntry entry)
{
    public CustomerLedgerEntry Entry { get; } = entry;

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
        LedgerEntryKind.Receipt => Entry.ReceiptMethod is { } method ? $"入金（{ReceiptMethodLabel(method)}）" : "入金",
        _ => string.Empty,
    };

    /// <summary>請求済みマーク（＊）。デモ画面の「請求」列を踏襲。</summary>
    public string BilledMark => Entry.BillingStatus == BillingLinkStatus.Billed ? "＊" : string.Empty;

    private static string ReceiptMethodLabel(ReceiptMethod method) => method switch
    {
        ReceiptMethod.Cash => "現金",
        ReceiptMethod.BankTransfer => "振込",
        ReceiptMethod.PromissoryNote => "手形",
        ReceiptMethod.Offset => "相殺",
        _ => string.Empty,
    };
}
