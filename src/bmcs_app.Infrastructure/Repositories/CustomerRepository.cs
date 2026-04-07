using System.Data;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task<IEnumerable<Customer>> GetAllAsync()
    {
        var list = new List<Customer>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT customer_id, customer_code, customer_name,
                   closing_day, tax_fraction_id, tax_calc_unit_id, employee_id,
                   postal_code, address1, address2
            FROM customers
            WHERE is_deleted = 0
            ORDER BY customer_code
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Customer
            {
                CustomerId    = reader.GetInt32(0),
                CustomerCode  = reader.GetString(1),
                CustomerName  = reader.GetString(2),
                ClosingDay    = reader.GetByte(3),
                TaxFractionId = reader.GetInt32(4),
                TaxCalcUnitId = reader.GetInt32(5),
                EmployeeId    = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                PostalCode    = reader.IsDBNull(7) ? null : reader.GetString(7),
                Address1      = reader.IsDBNull(8) ? null : reader.GetString(8),
                Address2      = reader.IsDBNull(9) ? null : reader.GetString(9),
            });
        }
        return list;
    }

    public async Task<IEnumerable<TaxFractionClassification>> GetTaxFractionsAsync()
    {
        var list = new List<TaxFractionClassification>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT tax_fraction_id, tax_fraction_code, tax_fraction_name
            FROM tax_fraction_classifications
            WHERE is_deleted = 0
            ORDER BY tax_fraction_id
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new TaxFractionClassification
            {
                TaxFractionId   = reader.GetInt32(0),
                TaxFractionCode = reader.GetString(1),
                TaxFractionName = reader.GetString(2),
            });
        }
        return list;
    }

    public async Task<IEnumerable<TaxCalcUnitClassification>> GetTaxCalcUnitsAsync()
    {
        var list = new List<TaxCalcUnitClassification>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT tax_calc_unit_id, tax_calc_unit_code, tax_calc_unit_name
            FROM tax_calc_unit_classifications
            WHERE is_deleted = 0
            ORDER BY tax_calc_unit_id
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new TaxCalcUnitClassification
            {
                TaxCalcUnitId   = reader.GetInt32(0),
                TaxCalcUnitCode = reader.GetString(1),
                TaxCalcUnitName = reader.GetString(2),
            });
        }
        return list;
    }

    public async Task<IEnumerable<Employee>> GetEmployeesAsync()
    {
        var list = new List<Employee>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT employee_id, employee_code, employee_name
            FROM employees
            WHERE is_deleted = 0
            ORDER BY employee_code
            """;
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

    public async Task UpsertAsync(int? customerId, string code, string name,
                                   byte closingDay, int taxFractionId, int taxCalcUnitId,
                                   int? employeeId,
                                   string? postalCode, string? address1, string? address2)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_customers_upsert";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@customer_id",      (object?)customerId  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@customer_code",    code);
        cmd.Parameters.AddWithValue("@customer_name",    name);
        cmd.Parameters.AddWithValue("@closing_day",      closingDay);
        cmd.Parameters.AddWithValue("@tax_fraction_id",  taxFractionId);
        cmd.Parameters.AddWithValue("@tax_calc_unit_id", taxCalcUnitId);
        cmd.Parameters.AddWithValue("@employee_id",      (object?)employeeId  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@postal_code",      (object?)postalCode  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@address1",         (object?)address1    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@address2",         (object?)address2    ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int customerId)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_customers_delete";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@customer_id", customerId);
        await cmd.ExecuteNonQueryAsync();
    }
}
