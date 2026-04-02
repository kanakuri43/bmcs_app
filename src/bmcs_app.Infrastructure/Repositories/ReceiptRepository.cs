using System.Data;
using System.Text.Json;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class ReceiptRepository : IReceiptRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task<IEnumerable<SlipSummary>> GetSummariesAsync()
    {
        var list = new List<SlipSummary>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT receipt_no,
                   MIN(receipt_date)   AS receipt_date,
                   MAX(customer_name)  AS customer_name
            FROM receipts
            WHERE is_deleted = 0
            GROUP BY receipt_no
            ORDER BY receipt_no
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

    public async Task<ReceiptSlip?> GetByReceiptNoAsync(string receiptNo)
    {
        ReceiptSlip? slip = null;
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_receipts_select";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@receipt_no", receiptNo);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (slip is null)
            {
                // usp_receipts_select は invoiced_at / ar_aggregated_at を含む → ロック判定はここで完結
                var invoicedAt     = reader.IsDBNull(reader.GetOrdinal("invoiced_at"))      ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("invoiced_at"));
                var arAggregatedAt = reader.IsDBNull(reader.GetOrdinal("ar_aggregated_at")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("ar_aggregated_at"));

                slip = new ReceiptSlip
                {
                    ReceiptNo    = reader.GetString(reader.GetOrdinal("receipt_no")),
                    ReceiptDate  = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("receipt_date"))),
                    CustomerId   = reader.GetInt32(reader.GetOrdinal("customer_id")),
                    CustomerCode = reader.GetString(reader.GetOrdinal("customer_code")),
                    CustomerName = reader.GetString(reader.GetOrdinal("customer_name")),
                    SlipRemarks  = reader.IsDBNull(reader.GetOrdinal("slip_remarks")) ? null : reader.GetString(reader.GetOrdinal("slip_remarks")),
                    IsLocked     = invoicedAt.HasValue || arAggregatedAt.HasValue,
                };
            }

            slip.Lines.Add(new ReceiptLine
            {
                LineNo            = reader.GetInt32(reader.GetOrdinal("line_no")),
                PaymentMethodId   = reader.GetInt32(reader.GetOrdinal("payment_method_id")),
                PaymentMethodName = reader.GetString(reader.GetOrdinal("payment_method_name")),
                Amount            = reader.GetDecimal(reader.GetOrdinal("amount")),
                LineRemarks       = reader.IsDBNull(reader.GetOrdinal("line_remarks")) ? null : reader.GetString(reader.GetOrdinal("line_remarks")),
            });
        }

        return slip;
    }

    public async Task UpsertAsync(
        string receiptNo, DateOnly receiptDate, int customerId,
        string? slipRemarks, IEnumerable<ReceiptLineInput> lines)
    {
        var linesJson = JsonSerializer.Serialize(lines.Select(l => new
        {
            line_no           = l.LineNo,
            payment_method_id = l.PaymentMethodId,
            amount            = l.Amount,
            line_remarks      = l.LineRemarks,
        }));

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_receipts_upsert";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@receipt_no",   receiptNo);
        cmd.Parameters.AddWithValue("@receipt_date", receiptDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@customer_id",  customerId);
        cmd.Parameters.AddWithValue("@slip_remarks", (object?)slipRemarks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lines",        linesJson);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string receiptNo)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_receipts_delete";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@receipt_no", receiptNo);
        await cmd.ExecuteNonQueryAsync();
    }
}
