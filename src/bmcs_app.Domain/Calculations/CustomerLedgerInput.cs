using bmcs_app.Domain.Entities;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// <see cref="CustomerLedgerBuilder.Build"/> の入力。DBアクセスは呼び出し元
/// （<c>CustomerLedgerQueryService</c>）の責務とし、本レコードは得意先の**全期間・未削除の全行**を
/// 受け取る（繰越を全期間積み上げで算出するため。TODO.md 8-1 D-4）。
/// </summary>
/// <param name="Customer">得意先マスタ。</param>
/// <param name="PeriodFrom">表示期間の開始日。</param>
/// <param name="PeriodTo">表示期間の終了日。</param>
/// <param name="SalesLines">得意先の全期間の売上明細行。</param>
/// <param name="ConfirmedBillings">
/// 確定済み（<see cref="Enums.BillingStatus.Confirmed"/>）の請求データ。<see cref="Enums.TaxUnit.Invoice"/>
/// の得意先でのみ使用する。解除済みは呼び出し元が除外して渡す（product-spec.md「解除済＝集計対象外」）。
/// </param>
/// <param name="ReceiptLines">締め得意先（<see cref="Enums.TaxUnit.Invoice"/>／<see cref="Enums.TaxUnit.Slip"/>）の入金明細行。</param>
/// <param name="DetailReceiptLines">都度得意先（<see cref="Enums.TaxUnit.Line"/>）の明細入金行。</param>
/// <param name="DetailInvoiceLinks">明細請求書と売上明細行の連携（都度得意先のみ使用）。</param>
/// <param name="DepositMethods">
/// 入金方法マスタの全行（無効化済みを含む。過去の入金が参照するコードの名称解決に使うため）。
/// </param>
public sealed record CustomerLedgerInput(
    Customer Customer,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyList<Sales> SalesLines,
    IReadOnlyList<Billing> ConfirmedBillings,
    IReadOnlyList<Receipt> ReceiptLines,
    IReadOnlyList<DetailReceipt> DetailReceiptLines,
    IReadOnlyList<DetailInvoiceSalesLine> DetailInvoiceLinks,
    IReadOnlyList<DepositMethod> DepositMethods);

/// <summary>
/// <see cref="CustomerLedgerBuilder.Build"/> の出力。<see cref="Entries"/> の先頭は前月繰越行。
/// ただし <see cref="IsTransactionHistoryOnly"/> が true（請求集約元。TODO.md 12-E）の場合は
/// 繰越行を持たず、<see cref="Entries"/> は売上行のみになる。
/// </summary>
/// <param name="SalesTotal">期間内の売上額計（返品・値引を含む純額。消費税は含まない）。</param>
/// <param name="ReceiptTotal">
/// 期間内の入金額計。エントリから直接積み上げる（前月繰越との差分から逆算する方式は採用しない）。
/// </param>
/// <param name="IsTransactionHistoryOnly">
/// 請求集約元（<c>!Customer.IsBillingRoot</c>）の取引履歴のみモード（TODO.md 12-E、
/// docs/design_document.md 28-2節 #6）。true のとき <see cref="OpeningBalance"/>／
/// <see cref="TaxTotal"/>／<see cref="ReceiptTotal"/>／<see cref="ClosingBalance"/> はすべて 0
/// （残高・繰越を持たない。入金・請求・売掛残高は請求集約先に集約されるため）。
/// </param>
public sealed record CustomerLedgerResult(
    IReadOnlyList<CustomerLedgerEntry> Entries,
    decimal OpeningBalance,
    decimal SalesTotal,
    decimal TaxTotal,
    decimal ReceiptTotal,
    decimal ClosingBalance,
    bool IsTransactionHistoryOnly = false)
{
    /// <summary>
    /// 検算用。常に true になること（単体テストで assert する）。取引履歴のみモードは
    /// 残高・繰越を計算しない（<see cref="IsTransactionHistoryOnly"/>）ため式の対象外とする。
    /// </summary>
    public bool IsBalanced
        => IsTransactionHistoryOnly || ClosingBalance == OpeningBalance + SalesTotal + TaxTotal - ReceiptTotal;
}
