using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 請求データ（billing）。明細行を持たないヘッダー1テーブル。主キーは BillingNumber。
/// 旧 BillingTaxUnitInvoice / BillingTaxUnitSlip の2テーブルを TaxUnit 列を持つ単一
/// テーブルに統合したもの（010_unify_tax_unit_tables.sql）。内税明細単位（都度得意先）は
/// 繰越残高の概念がなく請求データを持たないため（<see cref="DetailInvoice"/> が担う）、
/// TaxUnit は Invoice/Slip のみ。
/// </summary>
public class Billing : AuditableEntity
{
    public required string BillingNumber { get; set; }

    public required string CustomerCode { get; set; }

    /// <summary>Invoice/Slip のみ。</summary>
    public required TaxUnit TaxUnit { get; set; }

    public required string CustomerName { get; set; }

    public required DateOnly BillingDate { get; set; }

    /// <summary>締め対象年月（YYYYMM）。月次締めとの突き合わせに使う（FKではなく導出）。</summary>
    public required string ClosingYearMonth { get; set; }

    public required decimal PreviousBalance { get; set; }

    public required decimal ReceiptAmount { get; set; }

    public required decimal SalesAmount { get; set; }

    public required decimal TaxAmount { get; set; }

    public required decimal CurrentBillingAmount { get; set; }

    /// <summary>税率別内訳: 標準税率の対価額。</summary>
    public required decimal StandardRateTaxableAmount { get; set; }

    public required decimal StandardRateTaxAmount { get; set; }

    /// <summary>軽減税率の対価額。</summary>
    public required decimal ReducedRateTaxableAmount { get; set; }

    public required decimal ReducedRateTaxAmount { get; set; }

    public required decimal TaxExemptAmount { get; set; }

    public required BillingStatus BillingStatus { get; set; }

    public required DateTime ConfirmedAt { get; set; }

    public required string ConfirmedBy { get; set; }

    public DateTime? ReleasedAt { get; set; }

    public string? ReleasedBy { get; set; }
}
