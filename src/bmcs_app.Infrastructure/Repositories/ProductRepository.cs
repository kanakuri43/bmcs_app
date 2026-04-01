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
            SELECT product_id, product_code, product_name, tax_type_id, tax_rate_type
            FROM products
            WHERE is_deleted = 0
            ORDER BY product_code
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Product
            {
                ProductId   = reader.GetInt32(0),
                ProductCode = reader.GetString(1),
                ProductName = reader.GetString(2),
                TaxTypeId   = reader.GetInt32(3),
                TaxRateType = reader.GetByte(4),
            });
        }
        return list;
    }
}
