using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 売上（sales）。主キーは (SalesSlipNumber, LineNumber)。
/// 旧 SalesTaxUnitInvoice / SalesTaxUnitSlip / SalesTaxUnitLine の3テーブルを
/// TaxUnit 列を持つ単一テーブルに統合したもの（010_unify_tax_unit_tables.sql）。
/// 税単位固有の税額カラムは NULL 許容とし、TaxUnit との対応は DB の CHECK 制約
/// （CK_sales_tax_amount_by_tax_unit）で強制する。
/// </summary>
public class Sales : AuditableEntity
{
    public required string SalesSlipNumber { get; set; }

    public required short LineNumber { get; set; }

    public required DateOnly SlipDate { get; set; }

    public required string CustomerCode { get; set; }

    /// <summary>得意先マスタの税区分と、複合FKで常に一致することを保証する。</summary>
    public required TaxUnit TaxUnit { get; set; }

    public required string CustomerName { get; set; }

    public required SlipType SlipType { get; set; }

    public required string ProductCode { get; set; }

    public required string ProductName { get; set; }

    public string? Specification { get; set; }

    public string? UnitName { get; set; }

    /// <summary>数量。返品・値引はマイナス。</summary>
    public required decimal Quantity { get; set; }

    /// <summary>単価。TaxUnit=Line のとき内税単価、Invoice/Slip のとき外税単価のスナップショット。</summary>
    public required decimal UnitPrice { get; set; }

    public required decimal Amount { get; set; }

    public required decimal CostPrice { get; set; }

    public required TaxCategory TaxCategory { get; set; }

    public required decimal TaxRate { get; set; }

    /// <summary>伝票単位の税額。TaxUnit=Slip のときのみ値を持つ（同一伝票の全行に同値。SUM してはいけない）。</summary>
    public decimal? SlipTaxAmount { get; set; }

    /// <summary>行ごとの内税額。TaxUnit=Line のときのみ値を持つ（Amount は税込金額）。</summary>
    public decimal? TaxAmount { get; set; }

    /// <summary>納品書発行日時。NULL＝未発行（一括発行の対象）。</summary>
    public DateTime? DeliveryNoteIssuedAt { get; set; }

    public required short DeliveryNoteIssueCount { get; set; }

    /// <summary>売上明細行の請求への紐付け状態。請求データ自体の確定状態とは別概念。</summary>
    public required BillingLinkStatus BillingStatus { get; set; }

    public required SettlementStatus SettlementStatus { get; set; }

    public required decimal SettledAmount { get; set; }

    /// <summary>受注からの売上化の場合の受注伝票番号。</summary>
    public string? OrderSlipNumber { get; set; }

    public short? OrderLineNumber { get; set; }

    /// <summary>
    /// 締め請求データへの参照。TaxUnit=Invoice/Slip のときのみ使用し、NULL＝未請求。
    /// TaxUnit=Line は DetailInvoiceSalesLine 経由で明細請求書と紐付ける（このため常に NULL）。
    /// </summary>
    public string? BillingNumber { get; set; }

    /// <summary>伝票摘要。同一伝票の全行に同じ値が入る（伝票単位の値）。</summary>
    public string? SlipRemarks { get; set; }

    /// <summary>行摘要。</summary>
    public string? LineRemarks { get; set; }
}
