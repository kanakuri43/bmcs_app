using System.Data;
using System.Text.Json;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class InventoryCountRepository : IInventoryCountRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task<IEnumerable<DateOnly>> GetAllDatesAsync()
    {
        var dates = new List<DateOnly>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT count_date FROM inventory_counts ORDER BY count_date";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            dates.Add(DateOnly.FromDateTime(reader.GetDateTime(0)));
        return dates;
    }

    public async Task<IEnumerable<InventoryCountLine>> GetByDateAsync(DateOnly date)
    {
        var lines = new List<InventoryCountLine>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ic.inventory_count_id, ic.count_date, ic.product_id,
                   p.product_code, p.product_name, ic.quantity, ic.note
            FROM   inventory_counts ic
            JOIN   products p ON ic.product_id = p.product_id
            WHERE  ic.count_date = @count_date
            ORDER  BY ic.inventory_count_id";
        cmd.Parameters.AddWithValue("@count_date", date.ToDateTime(TimeOnly.MinValue));
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(new InventoryCountLine
            {
                InventoryCountId = reader.GetInt32(0),
                CountDate        = DateOnly.FromDateTime(reader.GetDateTime(1)),
                ProductId        = reader.GetInt32(2),
                ProductCode      = reader.GetString(3),
                ProductName      = reader.GetString(4),
                Quantity         = reader.GetDecimal(5),
                Note             = reader.IsDBNull(6) ? null : reader.GetString(6),
            });
        }
        return lines;
    }

    public async Task UpsertAsync(DateOnly date, IEnumerable<InventoryCountLineInput> lines)
    {
        var json = JsonSerializer.Serialize(lines.Select(l => new
        {
            product_id = l.ProductId,
            quantity   = l.Quantity,
            note       = l.Note,
        }));
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_inventory_count_upsert";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@count_date", date.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@lines", json);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteByDateAsync(DateOnly date)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_inventory_count_delete";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@count_date", date.ToDateTime(TimeOnly.MinValue));
        await cmd.ExecuteNonQueryAsync();
    }
}
