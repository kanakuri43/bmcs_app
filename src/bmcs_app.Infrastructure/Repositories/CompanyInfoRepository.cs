using System.Data;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class CompanyInfoRepository
{
    public async Task<CompanyInfo> GetAsync()
    {
        await using var conn = new SqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP 1
                company_name,
                address,
                tel,
                fax,
                invoice_no,
                bank_account_number1,
                bank_account_number2,
                bank_account_number3
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
                BankAccountNumber1    = r.IsDBNull(5) ? "" : r.GetString(5),
                BankAccountNumber2    = r.IsDBNull(6) ? "" : r.GetString(6),
                BankAccountNumber3    = r.IsDBNull(7) ? "" : r.GetString(7),
            };
        }

        return new CompanyInfo();
    }

    public async Task UpsertAsync(CompanyInfo info)
    {
        await using var conn = new SqlConnection(AppConfig.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_company_info_upsert";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@company_name",         info.Name);
        cmd.Parameters.AddWithValue("@address",              NullIfEmpty(info.Address));
        cmd.Parameters.AddWithValue("@tel",                  NullIfEmpty(info.Phone));
        cmd.Parameters.AddWithValue("@fax",                  NullIfEmpty(info.Fax));
        cmd.Parameters.AddWithValue("@invoice_no",           NullIfEmpty(info.InvoiceRegistrationNo));
        cmd.Parameters.AddWithValue("@bank_account_number1", NullIfEmpty(info.BankAccountNumber1));
        cmd.Parameters.AddWithValue("@bank_account_number2", NullIfEmpty(info.BankAccountNumber2));
        cmd.Parameters.AddWithValue("@bank_account_number3", NullIfEmpty(info.BankAccountNumber3));
        await cmd.ExecuteNonQueryAsync();
    }

    private static object NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}
