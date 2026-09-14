using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.ViewModels.Common;

/// <summary>銀行マスタ検索モーダルの一覧行の表示用。表示文字列の組み立ては Presentation 層の責務。</summary>
public sealed record BankAccountSearchItem(
    string BankAccountCode,
    string BankName,
    string BranchName,
    BankAccountType AccountType,
    string AccountTypeDisplay,
    string AccountNumber,
    string AccountHolderName)
{
    public static BankAccountSearchItem FromEntity(BankAccount bankAccount) => new(
        bankAccount.BankAccountCode,
        bankAccount.BankName,
        bankAccount.BranchName,
        bankAccount.AccountType,
        AccountTypeDisplayOf(bankAccount.AccountType),
        bankAccount.AccountNumber,
        bankAccount.AccountHolderName);

    private static string AccountTypeDisplayOf(BankAccountType accountType) => accountType switch
    {
        BankAccountType.Ordinary => "普通",
        BankAccountType.Checking => "当座",
        _ => accountType.ToString(),
    };
}
