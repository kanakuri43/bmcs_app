using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 締め入金（receipt）。主キーは (ReceiptSlipNumber, LineNumber)。各明細行は支払手段の内訳
/// （入金方法＋金額）を表す（docs/design_document.md 17章、2026-09-15改訂）。
/// 請求への充当は<see cref="ReceiptAllocation"/>が別途担い、このテーブルには持たない
/// （利用者にとって充当先は重要でなく、内部データとして自動計算されれば足りるため）。
/// 旧 ReceiptTaxUnitInvoice / ReceiptTaxUnitSlip の2テーブルを TaxUnit 列を持つ単一テーブルに
/// 統合したもの（010_unify_tax_unit_tables.sql）。内税明細単位（都度得意先）の入金は
/// 構造が異なるため <see cref="DetailReceipt"/> が担い、このテーブルには含めない。
/// </summary>
public class Receipt : AuditableEntity
{
    public required string ReceiptSlipNumber { get; set; }

    public required short LineNumber { get; set; }

    /// <summary>伝票単位の値（同一伝票の全行に同じ値が入る）。</summary>
    public required DateOnly ReceiptDate { get; set; }

    /// <summary>伝票単位の値。</summary>
    public required string CustomerCode { get; set; }

    /// <summary>伝票単位の値。Invoice/Slip のみ（Line の得意先の入金は DetailReceipt が担う）。</summary>
    public required TaxUnit TaxUnit { get; set; }

    /// <summary>伝票単位の値。スナップショット。</summary>
    public required string CustomerName { get; set; }

    /// <summary>行単位の値。この行の支払手段（<see cref="DepositMethod"/>マスタのコード）。</summary>
    public required string DepositMethodCode { get; set; }

    /// <summary>行単位の値。入金先口座。入金方法が口座指定を要する場合のみ使用。</summary>
    public string? BankAccountCode { get; set; }

    /// <summary>行単位の値。手形期日。入金方法が期日指定を要する場合のみ使用。</summary>
    public DateOnly? BillDueDate { get; set; }

    /// <summary>行単位の値。この行の入金額（伝票合計はSUMして求める）。</summary>
    public required decimal Amount { get; set; }

    /// <summary>
    /// 伝票単位のキャッシュ列。同一伝票の<see cref="ReceiptAllocation"/>の充当額合計と、
    /// 本テーブルの同一伝票のAmount合計との比較から<c>SettlementService</c>が導出する。
    /// </summary>
    public required AllocationStatus AllocationStatus { get; set; }

    /// <summary>伝票摘要。同一伝票の全行に同じ値が入る（伝票単位の値）。</summary>
    public string? SlipRemarks { get; set; }

    /// <summary>行摘要。</summary>
    public string? LineRemarks { get; set; }
}
