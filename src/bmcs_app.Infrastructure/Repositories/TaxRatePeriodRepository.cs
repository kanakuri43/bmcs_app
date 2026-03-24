using System.Data;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class TaxRatePeriodRepository : ITaxRatePeriodRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task<IEnumerable<TaxRatePeriod>> GetAllAsync()
    {
        var list = new List<TaxRatePeriod>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT tax_rate_period_id, start_date, end_date,
                   primary_tax_rate, secondary_tax_rate, tertiary_tax_rate
            FROM tax_rate_periods
            WHERE is_deleted = 0
            ORDER BY start_date DESC
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new TaxRatePeriod
            {
                TaxRatePeriodId  = reader.GetInt32(0),
                StartDate        = DateOnly.FromDateTime(reader.GetDateTime(1)),
                EndDate          = reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)),
                PrimaryTaxRate   = reader.GetDecimal(3),
                SecondaryTaxRate = reader.GetDecimal(4),
                TertiaryTaxRate  = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            });
        }
        return list;
    }

    public async Task UpsertAsync(int? taxRatePeriodId, DateOnly startDate, DateOnly? endDate,
                                   decimal primaryTaxRate, decimal secondaryTaxRate, decimal? tertiaryTaxRate)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_tax_rate_periods_upsert";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@tax_rate_period_id", (object?)taxRatePeriodId  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@start_date",         startDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@end_date",           endDate.HasValue ? (object)endDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@primary_tax_rate",   primaryTaxRate);
        cmd.Parameters.AddWithValue("@secondary_tax_rate", secondaryTaxRate);
        cmd.Parameters.AddWithValue("@tertiary_tax_rate",  (object?)tertiaryTaxRate  ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int taxRatePeriodId)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_tax_rate_periods_delete";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@tax_rate_period_id", taxRatePeriodId);
        await cmd.ExecuteNonQueryAsync();
    }
}
