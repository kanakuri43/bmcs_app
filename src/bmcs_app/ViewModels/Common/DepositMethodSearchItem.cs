using bmcs_app.Domain.Entities;

namespace bmcs_app.ViewModels.Common;

/// <summary>入金方法マスタ検索モーダルの一覧行の表示用。表示文字列の組み立ては Presentation 層の責務。</summary>
public sealed record DepositMethodSearchItem(
    string DepositMethodCode,
    string DepositMethodName,
    string RequiresBankAccountDisplay,
    string RequiresBillDueDateDisplay)
{
    public static DepositMethodSearchItem FromEntity(DepositMethod depositMethod) => new(
        depositMethod.DepositMethodCode,
        depositMethod.DepositMethodName,
        depositMethod.RequiresBankAccount ? "必須" : string.Empty,
        depositMethod.RequiresBillDueDate ? "必須" : string.Empty);
}
