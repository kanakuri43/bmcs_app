using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>銀行口座マスタ（bank_account）。自社の入金口座マスタとして解釈している（用途は要確認）。</summary>
public class BankAccount : AuditableEntity
{
    public required string BankAccountCode { get; set; }

    public required string BankName { get; set; }

    public required string BranchName { get; set; }

    public required BankAccountType AccountType { get; set; }

    public required string AccountNumber { get; set; }

    public required string AccountHolderName { get; set; }

    /// <summary>請求書・明細請求書に印字する口座かどうか。</summary>
    public required bool IsPrintOnInvoice { get; set; }

    public required short DisplayOrder { get; set; }
}
