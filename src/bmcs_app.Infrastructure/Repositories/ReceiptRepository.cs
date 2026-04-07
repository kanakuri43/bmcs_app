using System.Data;
using System.Text.Json;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class ReceiptRepository : IReceiptRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    /// <summary>
    /// 伝票検索ダイアログ用。入金明細を非正規化した全行を返す（直接 SQL）。
    /// 列順: 伝票日付, 伝票番号, 得意先コード, 得意先名, 行番号, 入金区分, 金額, 手形期日, 行摘要
    /// </summary>
    public async Task<IEnumerable<string[]>> GetAllFlatAsync()
    {
        var rows = new List<string[]>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT r.receipt_date, r.receipt_no,
                   r.customer_code, r.customer_name,
                   r.line_no, pm.payment_method_name, r.amount,
                   r.bill_due_date, r.line_remarks
            FROM   receipts r
            LEFT   JOIN payment_method_classifications pm
                   ON r.payment_method_id = pm.payment_method_id
            WHERE  r.is_deleted = 0
            ORDER  BY r.receipt_date DESC, r.receipt_no, r.line_no";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var billDueDate = reader.IsDBNull(7)
                ? ""
                : DateOnly.FromDateTime(reader.GetDateTime(7)).ToString("yyyy/MM/dd");
            rows.Add(new[]
            {
                DateOnly.FromDateTime(reader.GetDateTime(0)).ToString("yyyy/MM/dd"),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4).ToString(),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                reader.GetDecimal(6).ToString("#,##0"),
                billDueDate,
                reader.IsDBNull(8) ? "" : reader.GetString(8),
            });
        }
        return rows;
    }

    public async Task<IEnumerable<SlipSummary>> GetSummariesAsync()
    {
        var list = new List<SlipSummary>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_receipts_summaries_select";
        cmd.CommandType = CommandType.StoredProcedure;
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
                // invoiced_at / ar_aggregated_at は締め日付（date型）: どの締め期間に取り込まれたかを示す
                var invoicedAt     = reader.IsDBNull(reader.GetOrdinal("invoiced_at"))      ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("invoiced_at")));
                var arAggregatedAt = reader.IsDBNull(reader.GetOrdinal("ar_aggregated_at")) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("ar_aggregated_at")));

                slip = new ReceiptSlip
                {
                    ReceiptNo          = reader.GetString(reader.GetOrdinal("receipt_no")),
                    ReceiptDate        = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("receipt_date"))),
                    CustomerId         = reader.GetInt32(reader.GetOrdinal("customer_id")),
                    CustomerCode       = reader.GetString(reader.GetOrdinal("customer_code")),
                    CustomerName       = reader.GetString(reader.GetOrdinal("customer_name")),
                    CustomerPostalCode = reader.IsDBNull(reader.GetOrdinal("customer_postal_code")) ? null : reader.GetString(reader.GetOrdinal("customer_postal_code")),
                    CustomerAddress1   = reader.IsDBNull(reader.GetOrdinal("customer_address1"))    ? null : reader.GetString(reader.GetOrdinal("customer_address1")),
                    CustomerAddress2   = reader.IsDBNull(reader.GetOrdinal("customer_address2"))    ? null : reader.GetString(reader.GetOrdinal("customer_address2")),
                    SlipRemarks        = reader.IsDBNull(reader.GetOrdinal("slip_remarks")) ? null : reader.GetString(reader.GetOrdinal("slip_remarks")),
                    IsLocked           = invoicedAt.HasValue || arAggregatedAt.HasValue,
                };
            }

            var billDueDateOrd = reader.GetOrdinal("bill_due_date");
            slip.Lines.Add(new ReceiptLine
            {
                LineNo            = reader.GetInt32(reader.GetOrdinal("line_no")),
                PaymentMethodId   = reader.GetInt32(reader.GetOrdinal("payment_method_id")),
                PaymentMethodName = reader.GetString(reader.GetOrdinal("payment_method_name")),
                Amount            = reader.GetDecimal(reader.GetOrdinal("amount")),
                LineRemarks       = reader.IsDBNull(reader.GetOrdinal("line_remarks")) ? null : reader.GetString(reader.GetOrdinal("line_remarks")),
                BillDueDate       = reader.IsDBNull(billDueDateOrd) ? null : DateOnly.FromDateTime(reader.GetDateTime(billDueDateOrd)),
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
            bill_due_date     = l.BillDueDate.HasValue ? l.BillDueDate.Value.ToString("yyyy-MM-dd") : (string?)null,
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
