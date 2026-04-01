using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class TaxTypeRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task<IEnumerable<TaxTypeClassification>> GetAllAsync()
    {
        var list = new List<TaxTypeClassification>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT tax_type_id, tax_type_code, tax_type_name
            FROM tax_type_classifications
            WHERE is_deleted = 0
            ORDER BY tax_type_id
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new TaxTypeClassification
            {
                TaxTypeId   = reader.GetInt32(0),
                TaxTypeCode = reader.GetString(1),
                TaxTypeName = reader.GetString(2),
            });
        }
        return list;
    }
}
