namespace bmcs_app.Domain.Entities;

/// <summary>社員マスタ（employee）。</summary>
public class Employee : AuditableEntity
{
    public required string EmployeeCode { get; set; }

    public required string EmployeeName { get; set; }

    public string? EmployeeNameKana { get; set; }

    public required byte PermissionLevel { get; set; }
}
