using System.Data;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class ClosingRepository : IClosingRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task InvoiceClosingAsync(byte closingDay, DateOnly processDate, int? customerId = null)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_invoice_closing";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@closing_day",  closingDay);
        cmd.Parameters.AddWithValue("@process_date", processDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@customer_id",  (object?)customerId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InvoiceClosingCancelAsync(DateOnly processDate, int? customerId = null)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_invoice_closing_cancel";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@process_date", processDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@customer_id",  (object?)customerId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ArClosingAsync(DateOnly processDate, int? customerId = null)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_ar_closing";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@process_date", processDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@customer_id",  (object?)customerId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ArClosingCancelAsync(DateOnly processDate, int? customerId = null)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_ar_closing_cancel";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@process_date", processDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@customer_id",  (object?)customerId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<InvoiceHeader>> GetInvoiceHeadersAsync(
        DateOnly invoiceDate, byte closingDay, int? customerId = null)
    {
        var list = new List<InvoiceHeader>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ih.customer_id, ih.customer_name, ih.invoice_date,
                   ih.previous_invoice_amount, ih.receipt_amount,
                   ih.sales_amount_standard, ih.sales_amount_reduced,
                   ih.tax_amount_standard,   ih.tax_amount_reduced,
                   ih.current_invoice_amount
            FROM   invoice_headers ih
            JOIN   customers c ON ih.customer_id = c.customer_id AND c.is_deleted = 0
            WHERE  ih.invoice_date = @invoice_date
              AND  ih.is_deleted   = 0
              AND  c.closing_day   = @closing_day
              AND  (@customer_id IS NULL OR ih.customer_id = @customer_id)
            ORDER BY ih.customer_code
            """;
        cmd.Parameters.AddWithValue("@invoice_date", invoiceDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@closing_day",  closingDay);
        cmd.Parameters.AddWithValue("@customer_id",  (object?)customerId ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new InvoiceHeader
            {
                CustomerId            = reader.GetInt32(0),
                CustomerName          = reader.GetString(1),
                InvoiceDate           = DateOnly.FromDateTime(reader.GetDateTime(2)),
                PreviousInvoiceAmount = reader.GetDecimal(3),
                ReceiptAmount         = reader.GetDecimal(4),
                SalesAmountStandard   = reader.GetDecimal(5),
                SalesAmountReduced    = reader.GetDecimal(6),
                TaxAmountStandard     = reader.GetDecimal(7),
                TaxAmountReduced      = reader.GetDecimal(8),
                CurrentInvoiceAmount  = reader.GetDecimal(9),
            });
        }
        return list;
    }

    public async Task<IEnumerable<InvoiceSlipDetail>> GetInvoiceSlipDetailsAsync(
        DateOnly invoiceDate, int customerId)
    {
        var list = new List<InvoiceSlipDetail>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            ;WITH line_groups AS (
                SELECT s.sale_no, s.tax_type_id, s.applied_tax_rate,
                       SUM(s.quantity * s.unit_price) AS group_base
                FROM   sales s
                WHERE  s.is_deleted        = 0
                  AND  s.invoiced_at = @invoice_date
                  AND  s.customer_id       = @customer_id
                GROUP BY s.sale_no, s.tax_type_id, s.applied_tax_rate
            ),
            slip_tax AS (
                SELECT sale_no,
                       SUM(group_base) AS tax_excluded,
                       SUM(CASE WHEN tax_type_id = 1
                                THEN FLOOR(group_base * applied_tax_rate)
                                WHEN tax_type_id = 2
                                THEN FLOOR(group_base * applied_tax_rate / (1 + applied_tax_rate))
                                ELSE 0 END) AS tax_amount
                FROM   line_groups
                GROUP BY sale_no
            )
            SELECT st.sale_no,
                   MIN(s.sale_date)     AS sale_date,
                   MAX(s.slip_remarks)  AS slip_remarks,
                   st.tax_excluded,
                   st.tax_amount
            FROM   slip_tax st
            JOIN   sales s ON st.sale_no = s.sale_no
                           AND s.is_deleted = 0
                           AND s.invoiced_at = @invoice_date
                           AND s.customer_id = @customer_id
            GROUP BY st.sale_no, st.tax_excluded, st.tax_amount
            ORDER BY MIN(s.sale_date), st.sale_no
            """;
        cmd.Parameters.AddWithValue("@invoice_date", invoiceDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@customer_id",  customerId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new InvoiceSlipDetail
            {
                SaleNo      = reader.GetString(0),
                SaleDate    = DateOnly.FromDateTime(reader.GetDateTime(1)),
                Remarks     = reader.IsDBNull(2) ? "" : reader.GetString(2),
                TaxExcluded = reader.GetDecimal(3),
                TaxAmount   = reader.GetDecimal(4),
            });
        }
        return list;
    }

    public async Task<IEnumerable<InvoiceReceiptDetail>> GetInvoiceReceiptDetailsAsync(
        DateOnly invoiceDate, int customerId)
    {
        var list = new List<InvoiceReceiptDetail>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT MIN(receipt_date) AS receipt_date,
                   receipt_no,
                   MAX(ISNULL(slip_remarks, '')) AS slip_remarks,
                   SUM(amount) AS amount
            FROM   receipts
            WHERE  is_deleted  = 0
              AND  invoiced_at = @invoice_date
              AND  customer_id = @customer_id
            GROUP BY receipt_no
            ORDER BY MIN(receipt_date), receipt_no
            """;
        cmd.Parameters.AddWithValue("@invoice_date", invoiceDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@customer_id",  customerId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new InvoiceReceiptDetail
            {
                ReceiptDate = DateOnly.FromDateTime(reader.GetDateTime(0)),
                ReceiptNo   = reader.GetString(1),
                Remarks     = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Amount      = reader.GetDecimal(3),
            });
        }
        return list;
    }

    public async Task<IEnumerable<InvoiceTaxGroup>> GetInvoiceTaxGroupsAsync(
        DateOnly invoiceDate, int customerId)
    {
        var list = new List<InvoiceTaxGroup>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            ;WITH line_groups AS (
                SELECT s.tax_rate_type, s.tax_type_id, s.applied_tax_rate,
                       SUM(s.quantity * s.unit_price) AS group_base
                FROM   sales s
                WHERE  s.is_deleted        = 0
                  AND  s.invoiced_at = @invoice_date
                  AND  s.customer_id       = @customer_id
                GROUP BY s.sale_no, s.tax_rate_type, s.tax_type_id, s.applied_tax_rate
            )
            SELECT tax_rate_type, tax_type_id, applied_tax_rate,
                   SUM(group_base) AS tax_excluded,
                   SUM(CASE WHEN tax_type_id = 1
                            THEN FLOOR(group_base * applied_tax_rate)
                            WHEN tax_type_id = 2
                            THEN FLOOR(group_base * applied_tax_rate / (1 + applied_tax_rate))
                            ELSE 0 END) AS tax_amount
            FROM   line_groups
            GROUP BY tax_rate_type, tax_type_id, applied_tax_rate
            ORDER BY tax_rate_type, tax_type_id
            """;
        cmd.Parameters.AddWithValue("@invoice_date", invoiceDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@customer_id",  customerId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new InvoiceTaxGroup
            {
                TaxRateType    = reader.GetByte(0),
                TaxTypeId      = reader.GetInt32(1),
                AppliedTaxRate = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2),
                TaxExcluded    = reader.GetDecimal(3),
                TaxAmount      = reader.GetDecimal(4),
            });
        }
        return list;
    }
}
