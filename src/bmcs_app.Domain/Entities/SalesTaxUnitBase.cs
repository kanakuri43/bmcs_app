using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 売上（sales_tax_unit_invoice / sales_tax_unit_slip / sales_tax_unit_line）の共通構造。
/// 3テーブルとも明細行1テーブル構成（非正規化）。主キーは (SalesSlipNumber, LineNumber)。
/// 税額カラム・BillingNumber の持ち方はテーブルごとに異なるため、各派生クラスで定義する
/// （docs/database-schema.md 2.8「テーブルごとに異なるカラム」）。
/// </summary>
public abstract class SalesTaxUnitBase : AuditableEntity
{
    public required string SalesSlipNumber { get; set; }

    public required short LineNumber { get; set; }

    public required DateOnly SlipDate { get; set; }

    public required string CustomerCode { get; set; }

    public required string CustomerName { get; set; }

    public required SlipType SlipType { get; set; }

    public required string ProductCode { get; set; }

    public required string ProductName { get; set; }

    public string? Specification { get; set; }

    public string? UnitName { get; set; }

    /// <summary>数量。返品・値引はマイナス。</summary>
    public required decimal Quantity { get; set; }

    public required decimal UnitPrice { get; set; }

    public required decimal Amount { get; set; }

    public required decimal CostPrice { get; set; }

    public required TaxCategory TaxCategory { get; set; }

    public required decimal TaxRate { get; set; }

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
}
