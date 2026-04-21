using System.Data;
using bmcs_app.Core.Interfaces;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class InventoryCurrentRepository : IInventoryCurrentRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task<IEnumerable<InventoryCurrentStock>> GetAllAsync()
    {
        var rows = new List<InventoryCurrentStock>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_inventory_current_get";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@product_id", DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new InventoryCurrentStock
            {
                ProductId     = reader.GetInt32(reader.GetOrdinal("product_id")),
                ProductCode   = reader.GetString(reader.GetOrdinal("product_code")),
                ProductName   = reader.GetString(reader.GetOrdinal("product_name")),
                LastCountDate = reader.IsDBNull(reader.GetOrdinal("last_count_date"))
                    ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("last_count_date"))),
                LastCountQty  = reader.IsDBNull(reader.GetOrdinal("last_count_qty"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("last_count_qty")),
                PurchaseQty   = reader.GetDecimal(reader.GetOrdinal("purchase_qty")),
                SaleQty       = reader.GetDecimal(reader.GetOrdinal("sale_qty")),
                CurrentStock  = reader.IsDBNull(reader.GetOrdinal("current_stock"))
                    ? null : reader.GetDecimal(reader.GetOrdinal("current_stock")),
            });
        }
        return rows;
    }
}
