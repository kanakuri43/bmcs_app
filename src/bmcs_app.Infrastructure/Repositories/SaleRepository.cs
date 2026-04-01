using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class SaleRepository : ISaleRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task<IEnumerable<SlipSummary>> GetSummariesAsync()
    {
        var list = new List<SlipSummary>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT sale_no,
                   MIN(sale_date)     AS sale_date,
                   MAX(customer_name) AS customer_name
            FROM sales
            WHERE is_deleted = 0
            GROUP BY sale_no
            ORDER BY sale_no
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new SlipSummary
            {
                SlipNo       = reader.GetString(0),
                SlipDate     = DateOnly.FromDateTime(reader.GetDateTime(1)),
                CustomerName = reader.GetString(2),
            });
        }
        return list;
    }
}
