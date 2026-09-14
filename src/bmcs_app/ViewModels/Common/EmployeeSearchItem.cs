using bmcs_app.Domain.Entities;

namespace bmcs_app.ViewModels.Common;

/// <summary>社員マスタ検索モーダルの一覧行の表示用。表示文字列の組み立ては Presentation 層の責務。</summary>
public sealed record EmployeeSearchItem(
    string EmployeeCode,
    string EmployeeName,
    string? EmployeeNameKana,
    byte PermissionLevel)
{
    public static EmployeeSearchItem FromEntity(Employee employee) => new(
        employee.EmployeeCode,
        employee.EmployeeName,
        employee.EmployeeNameKana,
        employee.PermissionLevel);
}
