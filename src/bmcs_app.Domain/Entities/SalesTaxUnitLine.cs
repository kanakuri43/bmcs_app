namespace bmcs_app.Domain.Entities;

/// <summary>
/// 売上（内税明細単位＝都度得意先）。明細行ごとに税額を確定する。
/// 明細請求書との紐付けは連携テーブル（DetailInvoiceSalesLine）で行うため BillingNumber は持たない。
/// </summary>
public class SalesTaxUnitLine : SalesTaxUnitBase
{
    /// <summary>行ごとの内税額。Amount は税込金額。</summary>
    public required decimal TaxAmount { get; set; }
}
