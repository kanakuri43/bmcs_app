using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 明細入金（detail_payment）。明細単位（都度得意先）の入金。
/// 締め入金と異なり充当先が2種類ある（売上明細行を直接指定／明細請求書を指定）。
/// どちらが埋まっているかは <see cref="TargetType"/> と DB の CHECK 制約で一致させる。
/// </summary>
public class DetailPayment : AuditableEntity
{
    public required string DetailPaymentNumber { get; set; }

    public required short LineNumber { get; set; }

    public required DateOnly PaymentDate { get; set; }

    public required string CustomerCode { get; set; }

    public required string CustomerName { get; set; }

    public required PaymentMethod PaymentMethod { get; set; }

    public string? BankAccountCode { get; set; }

    public required decimal PaymentAmount { get; set; }

    public required DetailPaymentTargetType TargetType { get; set; }

    /// <summary>TargetType=SalesLine のとき使用。</summary>
    public string? TargetSalesSlipNumber { get; set; }

    public short? TargetSalesLineNumber { get; set; }

    /// <summary>TargetType=DetailInvoice のとき使用。</summary>
    public string? TargetDetailInvoiceNumber { get; set; }

    public required decimal AllocatedAmount { get; set; }

    public required decimal FeeAdjustmentAmount { get; set; }

    public required AllocationStatus AllocationStatus { get; set; }
}
