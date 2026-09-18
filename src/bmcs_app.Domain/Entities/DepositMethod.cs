namespace bmcs_app.Domain.Entities;

/// <summary>
/// 入金方法マスタ（deposit_method）。現金・振込・手形・相殺等、利用者が自由に追加・編集できる
/// （旧 ReceiptMethod enum を廃止し、マスタ駆動に置き換えたもの。2026-09-18決定）。
/// </summary>
public class DepositMethod : AuditableEntity
{
    public required string DepositMethodCode { get; set; }

    public required string DepositMethodName { get; set; }

    /// <summary>この入金方法を選んだ行に入金先口座（銀行口座）の指定を要するか。</summary>
    public required bool RequiresBankAccount { get; set; }

    /// <summary>この入金方法を選んだ行に手形期日の指定を要するか。</summary>
    public required bool RequiresBillDueDate { get; set; }

    public required short DisplayOrder { get; set; }
}
