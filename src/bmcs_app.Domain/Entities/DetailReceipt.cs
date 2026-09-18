using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 明細入金（detail_receipt）。明細単位（都度得意先）の入金。
/// 締め入金と異なり充当先が2種類ある（売上明細行を直接指定／明細請求書を指定）。
/// どちらが埋まっているかは <see cref="TargetType"/> と DB の CHECK 制約で一致させる。
/// </summary>
public class DetailReceipt : AuditableEntity
{
    public required string DetailReceiptNumber { get; set; }

    public required short LineNumber { get; set; }

    public required DateOnly ReceiptDate { get; set; }

    public required string CustomerCode { get; set; }

    public required string CustomerName { get; set; }

    /// <summary>この行の支払手段（<see cref="DepositMethod"/>マスタのコード）。</summary>
    public required string DepositMethodCode { get; set; }

    public string? BankAccountCode { get; set; }

    public required decimal ReceiptAmount { get; set; }

    public required DetailReceiptTargetType TargetType { get; set; }

    /// <summary>TargetType=SalesLine のとき使用。</summary>
    public string? TargetSalesSlipNumber { get; set; }

    public short? TargetSalesLineNumber { get; set; }

    /// <summary>TargetType=DetailInvoice のとき使用。</summary>
    public string? TargetDetailInvoiceNumber { get; set; }

    public required decimal AllocatedAmount { get; set; }

    public required decimal FeeAdjustmentAmount { get; set; }

    public required AllocationStatus AllocationStatus { get; set; }

    /// <summary>伝票摘要。同一伝票の全行に同じ値が入る（伝票単位の値）。</summary>
    public string? SlipRemarks { get; set; }

    /// <summary>行摘要。</summary>
    public string? LineRemarks { get; set; }
}
