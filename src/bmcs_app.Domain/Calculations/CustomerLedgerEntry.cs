using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 得意先元帳の1行（TODO.md 8-1）。左側（売上・消費税）と右側（入金）が同居しうる
/// （都度得意先の消込済み売上行は、消込の証跡として同じ行の右側に入金日付・入金Noを表示する。
/// ただし残高への影響は入金日付の独立行（<see cref="LedgerEntryKind.Receipt"/>）でのみ発生させる
/// ため、証跡表示では <see cref="ReceiptAmount"/> を設定しない。売上と入金が月をまたいでも
/// 各月末の売掛残高が日付どおり正確になる。docs/design_document.md 21章参照）。
///
/// 金額・日付・数量はすべて null 許容にする。XAML の StringFormat が null を空文字で描画するため、
/// 「売上側を空欄にした行」「入金側が無い行」を専用コンバーターなしで表現できる。
/// </summary>
public sealed record CustomerLedgerEntry
{
    public required LedgerEntryKind Kind { get; init; }

    /// <summary>
    /// 行の時系列位置を決める基準日。売上行＝<c>slip_date</c>、消費税行＝<c>billing_date</c>／
    /// 伝票日付／期間To、入金行＝<c>receipt_date</c>、繰越行＝期間Fromの前日。
    /// </summary>
    public required DateOnly EntryDate { get; init; }

    // ---- 左側（売上・消費税） ----
    public SlipType? SlipType { get; init; }
    public string? SalesSlipNumber { get; init; }
    public short? SalesLineNumber { get; init; }
    public string? ProductCode { get; init; }
    public string? ProductName { get; init; }
    public TaxCategory? TaxCategory { get; init; }
    public decimal? TaxRate { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }

    /// <summary>借方（残高を増やす額）。売上行＝<c>amount</c>、消費税行＝税額。返品・値引はマイナス。</summary>
    public decimal? DebitAmount { get; init; }

    /// <summary>内税明細単位（tax_unit=3）の内訳税額（<c>sales.tax_amount</c>）。参考表示のみで残高に影響しない。</summary>
    public decimal? IncludedTaxAmount { get; init; }

    /// <summary>売上明細行の請求への紐付け状態。</summary>
    public BillingLinkStatus? BillingStatus { get; init; }

    /// <summary>締め＝<c>billing_number</c>。未請求＝null。</summary>
    public string? BillingNumber { get; init; }

    public SettlementStatus? SettlementStatus { get; init; }

    // ---- 右側（入金） ----
    public DateOnly? ReceiptDate { get; init; }
    public string? ReceiptSlipNumber { get; init; }
    public short? ReceiptLineNumber { get; init; }
    public ReceiptMethod? ReceiptMethod { get; init; }

    /// <summary>
    /// 貸方（残高を減らす額）。<see cref="Kind"/> が <see cref="LedgerEntryKind.Receipt"/> の
    /// 独立行にのみ設定する。売上行に同居する入金の証跡表示では null のまま（残高に影響させない）。
    /// </summary>
    public decimal? ReceiptAmount { get; init; }

    // ---- 共通 ----

    /// <summary>この行の時点の残高（税込）。<see cref="CustomerLedgerBuilder"/> が時系列に累積して設定する。</summary>
    public decimal Balance { get; init; }

    public string? Remarks { get; init; }

    /// <summary>残高への影響額。繰越行は 0（<see cref="Balance"/> に直接値が入るため）。</summary>
    public decimal BalanceDelta
        => Kind == LedgerEntryKind.OpeningBalance
            ? 0m
            : (DebitAmount ?? 0m) - (ReceiptAmount ?? 0m);
}
