using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class PaymentMethodRepository : IPaymentMethodRepository
{
    public async Task<IEnumerable<PaymentMethod>> GetAllAsync()
    {
        var list = new List<PaymentMethod>();
        await using var conn = new SqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT payment_method_id, payment_method_code, payment_method_name
            FROM payment_method_classifications
            WHERE is_deleted = 0
            ORDER BY payment_method_code
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PaymentMethod
            {
                PaymentMethodId   = reader.GetInt32(0),
                PaymentMethodCode = reader.GetString(1),
                PaymentMethodName = reader.GetString(2),
            });
        }
        return list;
    }
}
