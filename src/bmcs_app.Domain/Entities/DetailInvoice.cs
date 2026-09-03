using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 明細請求書（detail_invoice）。明細行を持たないヘッダー1テーブル。
/// 明細部分は連携テーブル（DetailInvoiceSalesLine）経由で売上ジャーナルから組み立てる。
/// </summary>
public class DetailInvoice : AuditableEntity
{
    public required string DetailInvoiceNumber { get; set; }

    /// <summary>発行元となる正式な得意先。</summary>
    public required string CustomerCode { get; set; }

    public required string CustomerName { get; set; }

    /// <summary>請求書に印字する宛名。都度入力のスナップショット（学校のクラス・先生単位など）。</summary>
    public required string AddresseeName { get; set; }

    public required DateOnly IssueDate { get; set; }

    public required decimal SalesAmount { get; set; }

    public required decimal TaxAmount { get; set; }

    public required decimal TotalAmount { get; set; }

    public required decimal StandardRateTaxableAmount { get; set; }

    public required decimal StandardRateTaxAmount { get; set; }

    public required decimal ReducedRateTaxableAmount { get; set; }

    public required decimal ReducedRateTaxAmount { get; set; }

    public required decimal TaxExemptAmount { get; set; }

    public required DetailInvoiceStatus InvoiceStatus { get; set; }

    public required DateTime IssuedAt { get; set; }

    public required string IssuedBy { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancelledBy { get; set; }
}
