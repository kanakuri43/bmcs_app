namespace bmcs_app.Application.Common;

/// <summary>
/// 暫定実装。Phase 0-7（起動時パラメータでの社員コード受け取り）で差し替える。
/// それまでは seed データに実在する社員コードを固定で返す（docs/architecture.md 14章）。
/// </summary>
public class PlaceholderCurrentEmployeeContext : ICurrentEmployeeContext
{
    public string EmployeeCode => "EMP001";
}
