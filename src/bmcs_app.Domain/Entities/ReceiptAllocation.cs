using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 締め入金の請求への充当（receipt_allocation）。主キーは (ReceiptSlipNumber, LineNumber)。
/// <see cref="Receipt"/>の明細行（支払手段の内訳）とは独立した行番号体系を持つ。
/// <c>ReceiptEntryService</c>が入金額（<see cref="Receipt.Amount"/>の合計）を確定済み請求へ
/// 古い順に自動配分して生成する内部データであり、画面には表示しない（利用者にとって重要なのは
/// 充当先ではなく残高のため。docs/design_document.md 17章、2026-09-15改訂）。
/// </summary>
public class ReceiptAllocation : AuditableEntity
{
    public required string ReceiptSlipNumber { get; set; }

    public required short LineNumber { get; set; }

    /// <summary>非正規化。SettlementService が得意先単位で充当行を引くために持つ。</summary>
    public required string CustomerCode { get; set; }

    public required TaxUnit TaxUnit { get; set; }

    /// <summary>充当先の請求データ。NULL＝前受・過入金（充当先未定）。</summary>
    public string? BillingNumber { get; set; }

    public required decimal AllocatedAmount { get; set; }

    public required decimal FeeAdjustmentAmount { get; set; }
}
