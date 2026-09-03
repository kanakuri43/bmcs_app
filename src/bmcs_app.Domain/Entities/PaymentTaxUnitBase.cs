using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 締め入金（payment_tax_unit_invoice / payment_tax_unit_slip）の共通構造。
/// 2テーブルは共通構造（docs/database-schema.md 2.9）。
/// 主キーは (PaymentSlipNumber, LineNumber)。各明細行が1件の充当を表す。
/// </summary>
public abstract class PaymentTaxUnitBase : AuditableEntity
{
    public required string PaymentSlipNumber { get; set; }

    public required short LineNumber { get; set; }

    public required DateOnly PaymentDate { get; set; }

    public required string CustomerCode { get; set; }

    public required string CustomerName { get; set; }

    public required PaymentMethod PaymentMethod { get; set; }

    /// <summary>入金先口座。振込のとき使用。</summary>
    public string? BankAccountCode { get; set; }

    /// <summary>入金額（伝票単位の値。SUM してはいけない）。</summary>
    public required decimal PaymentAmount { get; set; }

    /// <summary>充当先の請求データ。NULL＝前受・過入金（充当先未定）。</summary>
    public string? BillingNumber { get; set; }

    public required decimal AllocatedAmount { get; set; }

    public required decimal FeeAdjustmentAmount { get; set; }

    public required AllocationStatus AllocationStatus { get; set; }
}
