using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class EmployeeRepository : IEmployeeRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task<IEnumerable<Employee>> GetAllAsync()
    {
        var list = new List<Employee>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT employee_id, employee_code, employee_name FROM employees WHERE is_deleted = 0 ORDER BY employee_code";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Employee
            {
                EmployeeId   = reader.GetInt32(0),
                EmployeeCode = reader.GetString(1),
                EmployeeName = reader.GetString(2),
            });
        }
        return list;
    }

    public async Task UpsertAsync(int? employeeId, string code, string name)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_employees_upsert";
        cmd.CommandType = System.Data.CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@employee_id", (object?)employeeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@employee_code", code);
        cmd.Parameters.AddWithValue("@employee_name", name);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int employeeId)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_employees_delete";
        cmd.CommandType = System.Data.CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@employee_id", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }
}
