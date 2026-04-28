using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        var list = new List<Product>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT product_id, product_code, product_name, tax_type_id, tax_rate_type, cost_price, is_miscellaneous
            FROM products
            WHERE is_deleted = 0
            ORDER BY product_code
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Product
            {
                ProductId       = reader.GetInt32(0),
                ProductCode     = reader.GetString(1),
                ProductName     = reader.GetString(2),
                TaxTypeId       = reader.GetInt32(3),
                TaxRateType     = reader.GetByte(4),
                CostPrice       = reader.GetDecimal(5),
                IsMiscellaneous = reader.GetBoolean(6),
            });
        }
        return list;
    }

    public async Task UpsertAsync(int? productId, string code, string name, int taxTypeId, byte taxRateType, decimal costPrice, bool isMiscellaneous = false)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_products_upsert";
        cmd.CommandType = System.Data.CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@product_id",        (object?)productId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@product_code",      code);
        cmd.Parameters.AddWithValue("@product_name",      name);
        cmd.Parameters.AddWithValue("@tax_type_id",       taxTypeId);
        cmd.Parameters.AddWithValue("@tax_rate_type",     taxRateType);
        cmd.Parameters.AddWithValue("@cost_price",        costPrice);
        cmd.Parameters.AddWithValue("@is_miscellaneous",  isMiscellaneous);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int productId)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_products_delete";
        cmd.CommandType = System.Data.CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@product_id", productId);
        await cmd.ExecuteNonQueryAsync();
    }
}
