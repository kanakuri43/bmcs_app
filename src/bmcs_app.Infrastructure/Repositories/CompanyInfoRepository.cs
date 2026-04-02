using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class CompanyInfoRepository
{
    public async Task<CompanyInfo> GetAsync()
    {
        var cs = AppConfig.ConnectionString;
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP 1
                company_name,
                address,
                tel,
                fax,
                invoice_no
            FROM company_info
            ORDER BY company_info_id";

        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            return new CompanyInfo
            {
                Name                  = r.IsDBNull(0) ? "" : r.GetString(0),
                Address               = r.IsDBNull(1) ? "" : r.GetString(1),
                Phone                 = r.IsDBNull(2) ? "" : r.GetString(2),
                Fax                   = r.IsDBNull(3) ? "" : r.GetString(3),
                InvoiceRegistrationNo = r.IsDBNull(4) ? "" : r.GetString(4),
            };
        }

        return new CompanyInfo();
    }
}
