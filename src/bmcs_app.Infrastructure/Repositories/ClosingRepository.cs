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

    public async Task<IEnumerable<ArHistorySummary>> GetArHistorySummariesAsync()
    {
        var list = new List<ArHistorySummary>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_ar_closing_history_select";
        cmd.CommandType = CommandType.StoredProcedure;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ArHistorySummary
            {
                ClosingDate   = DateOnly.FromDateTime(reader.GetDateTime(0)),
                CustomerCount = reader.GetInt32(1),
            });
        }
        return list;
    }

    public async Task<IEnumerable<InvoiceHistorySummary>> GetInvoiceHistorySummariesAsync()
    {
        var list = new List<InvoiceHistorySummary>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_invoice_closing_history_select";
        cmd.CommandType = CommandType.StoredProcedure;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new InvoiceHistorySummary
            {
                InvoiceDate   = DateOnly.FromDateTime(reader.GetDateTime(0)),
                CustomerCount = reader.GetInt32(1),
            });
        }
        return list;
    }

    public async Task<IEnumerable<InvoiceHeader>> GetInvoiceHeadersAsync(
        DateOnly invoiceDate, byte closingDay, int? customerId = null)
    {
        var list = new List<InvoiceHeader>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_invoice_headers_select";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@invoice_date", invoiceDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@closing_day",  closingDay);
        cmd.Parameters.AddWithValue("@customer_id",  (object?)customerId ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new InvoiceHeader
            {
                CustomerId            = reader.GetInt32(reader.GetOrdinal("customer_id")),
                CustomerCode          = reader.GetString(reader.GetOrdinal("customer_code")),
                CustomerName          = reader.GetString(reader.GetOrdinal("customer_name")),
                InvoiceDate           = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("invoice_date"))),
                PreviousInvoiceAmount = reader.GetDecimal(reader.GetOrdinal("previous_invoice_amount")),
                ReceiptAmount         = reader.GetDecimal(reader.GetOrdinal("receipt_amount")),
                SalesAmountStandard   = reader.GetDecimal(reader.GetOrdinal("sales_amount_standard")),
                SalesAmountReduced    = reader.GetDecimal(reader.GetOrdinal("sales_amount_reduced")),
                TaxAmountStandard     = reader.GetDecimal(reader.GetOrdinal("tax_amount_standard")),
                TaxAmountReduced      = reader.GetDecimal(reader.GetOrdinal("tax_amount_reduced")),
                CurrentInvoiceAmount  = reader.GetDecimal(reader.GetOrdinal("current_invoice_amount")),
                CustomerPostalCode    = reader.IsDBNull(reader.GetOrdinal("customer_postal_code")) ? null : reader.GetString(reader.GetOrdinal("customer_postal_code")),
                CustomerAddress1      = reader.IsDBNull(reader.GetOrdinal("customer_address1"))    ? null : reader.GetString(reader.GetOrdinal("customer_address1")),
                CustomerAddress2      = reader.IsDBNull(reader.GetOrdinal("customer_address2"))    ? null : reader.GetString(reader.GetOrdinal("customer_address2")),
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
        cmd.CommandText = "usp_invoice_slip_details_select";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@invoice_date", invoiceDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@customer_id",  customerId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new InvoiceSlipDetail
            {
                SaleDate    = DateOnly.FromDateTime(reader.GetDateTime(0)),
                SaleNo      = reader.GetString(1),
                LineNo      = reader.GetInt32(2),
                ProductName = reader.GetString(3),
                Quantity    = reader.GetDecimal(4),
                UnitPrice   = reader.GetDecimal(5),
                LineAmount  = reader.GetDecimal(6),
                LineRemarks = reader.IsDBNull(7) ? null : reader.GetString(7),
                TaxRateType = reader.GetByte(8),
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
        cmd.CommandText = "usp_invoice_receipt_details_select";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@invoice_date", invoiceDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@customer_id",  customerId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new InvoiceReceiptDetail
            {
                ReceiptDate       = DateOnly.FromDateTime(reader.GetDateTime(0)),
                ReceiptNo         = reader.GetString(1),
                LineNo            = reader.GetInt32(2),
                PaymentMethodName = reader.GetString(3),
                Amount            = reader.GetDecimal(4),
                LineRemarks       = reader.IsDBNull(5) ? null : reader.GetString(5),
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
        cmd.CommandText = "usp_invoice_tax_groups_select";
        cmd.CommandType = CommandType.StoredProcedure;
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
