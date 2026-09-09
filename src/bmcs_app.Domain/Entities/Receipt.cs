using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 締め入金（receipt）。主キーは (ReceiptSlipNumber, LineNumber)。各明細行が1件の充当を表す。
/// 旧 ReceiptTaxUnitInvoice / ReceiptTaxUnitSlip の2テーブルを TaxUnit 列を持つ単一テーブルに
/// 統合したもの（010_unify_tax_unit_tables.sql）。内税明細単位（都度得意先）の入金は
/// 構造が異なるため <see cref="DetailReceipt"/> が担い、このテーブルには含めない。
/// </summary>
public class Receipt : AuditableEntity
{
    public required string ReceiptSlipNumber { get; set; }

    public required short LineNumber { get; set; }

    public required DateOnly ReceiptDate { get; set; }

    public required string CustomerCode { get; set; }

    /// <summary>Invoice/Slip のみ（Line の得意先の入金は DetailReceipt が担う）。</summary>
    public required TaxUnit TaxUnit { get; set; }

    public required string CustomerName { get; set; }

    public required ReceiptMethod ReceiptMethod { get; set; }

    /// <summary>入金先口座。振込のとき使用。</summary>
    public string? BankAccountCode { get; set; }

    /// <summary>入金額（伝票単位の値。SUM してはいけない）。</summary>
    public required decimal ReceiptAmount { get; set; }

    /// <summary>充当先の請求データ。NULL＝前受・過入金（充当先未定）。</summary>
    public string? BillingNumber { get; set; }

    public required decimal AllocatedAmount { get; set; }

    public required decimal FeeAdjustmentAmount { get; set; }

    public required AllocationStatus AllocationStatus { get; set; }

    /// <summary>伝票摘要。同一伝票の全行に同じ値が入る（伝票単位の値）。</summary>
    public string? SlipRemarks { get; set; }

    /// <summary>行摘要。</summary>
    public string? LineRemarks { get; set; }
}
