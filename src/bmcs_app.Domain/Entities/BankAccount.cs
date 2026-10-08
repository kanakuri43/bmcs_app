using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 銀行口座マスタ（bank_account）。自社の振込先口座マスタ。得意先マスタから
/// 最大2件（<see cref="Customer.BankAccountCode1"/>／<see cref="Customer.BankAccountCode2"/>）
/// 紐づけて請求書へ印字する（得意先ごとに使い分ける）。
/// </summary>
public class BankAccount : AuditableEntity
{
    public required string BankAccountCode { get; set; }

    public required string BankName { get; set; }

    public required string BranchName { get; set; }

    public required BankAccountType AccountType { get; set; }

    public required string AccountNumber { get; set; }

    public required string AccountHolderName { get; set; }

    public required short DisplayOrder { get; set; }
}
