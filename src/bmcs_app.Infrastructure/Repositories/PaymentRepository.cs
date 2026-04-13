using System.Data;
using System.Text.Json;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    /// <summary>
    /// 伝票検索ダイアログ用。支払明細を非正規化した全行を返す（直接 SQL）。
    /// 列順: 支払日付, 支払番号, 仕入先コード, 仕入先名, 行番号, 支払区分, 金額, 手形期日, 行摘要
    /// </summary>
    public async Task<IEnumerable<string[]>> GetAllFlatAsync()
    {
        var rows = new List<string[]>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.payment_date, p.payment_no,
                   p.supplier_code, p.supplier_name,
                   p.line_no, pm.payment_method_name, p.amount,
                   p.bill_due_date, p.line_remarks
            FROM   payments p
            LEFT   JOIN payment_method_classifications pm
                   ON p.payment_method_id = pm.payment_method_id
            WHERE  p.is_deleted = 0
            ORDER  BY p.payment_date DESC, p.payment_no, p.line_no";
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
        cmd.CommandText = "usp_payments_summaries_select";
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

    public async Task<PaymentSlip?> GetByPaymentNoAsync(string paymentNo)
    {
        PaymentSlip? slip = null;
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_payments_select";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@payment_no", paymentNo);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (slip is null)
            {
                var apClosingAt = reader.IsDBNull(reader.GetOrdinal("ap_closing_at"))
                    ? (DateOnly?)null
                    : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("ap_closing_at")));

                slip = new PaymentSlip
                {
                    PaymentNo          = reader.GetString(reader.GetOrdinal("payment_no")),
                    PaymentDate        = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("payment_date"))),
                    SupplierId         = reader.GetInt32(reader.GetOrdinal("supplier_id")),
                    SupplierCode       = reader.GetString(reader.GetOrdinal("supplier_code")),
                    SupplierName       = reader.GetString(reader.GetOrdinal("supplier_name")),
                    SupplierPostalCode = reader.IsDBNull(reader.GetOrdinal("supplier_postal_code")) ? null : reader.GetString(reader.GetOrdinal("supplier_postal_code")),
                    SupplierAddress1   = reader.IsDBNull(reader.GetOrdinal("supplier_address1"))    ? null : reader.GetString(reader.GetOrdinal("supplier_address1")),
                    SupplierAddress2   = reader.IsDBNull(reader.GetOrdinal("supplier_address2"))    ? null : reader.GetString(reader.GetOrdinal("supplier_address2")),
                    SlipRemarks        = reader.IsDBNull(reader.GetOrdinal("slip_remarks")) ? null : reader.GetString(reader.GetOrdinal("slip_remarks")),
                    IsLocked           = apClosingAt.HasValue,
                };
            }

            var billDueDateOrd = reader.GetOrdinal("bill_due_date");
            slip.Lines.Add(new PaymentLine
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
        string paymentNo, DateOnly paymentDate, int supplierId,
        string? slipRemarks, IEnumerable<PaymentLineInput> lines)
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
        cmd.CommandText = "usp_payments_upsert";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@payment_no",   paymentNo);
        cmd.Parameters.AddWithValue("@payment_date", paymentDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@supplier_id",  supplierId);
        cmd.Parameters.AddWithValue("@slip_remarks", (object?)slipRemarks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lines",        linesJson);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string paymentNo)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_payments_delete";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@payment_no", paymentNo);
        await cmd.ExecuteNonQueryAsync();
    }
}
