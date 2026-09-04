namespace bmcs_app.Application.Common;

/// <summary>現在操作している社員を解決する。監査列（CreatedBy/UpdatedBy）の値に使う。</summary>
public interface ICurrentEmployeeContext
{
    /// <summary>現在操作している社員コード（employee.employee_code）。</summary>
    string EmployeeCode { get; }
}
