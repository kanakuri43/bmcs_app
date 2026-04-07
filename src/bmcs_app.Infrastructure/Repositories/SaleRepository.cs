using System.Data;
using System.Text.Json;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class SaleRepository : ISaleRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    /// <summary>
    /// 伝票検索ダイアログ用。売上明細を非正規化した全行を返す（直接 SQL）。
    /// 列順: 伝票日付, 伝票番号, 得意先コード, 得意先名, 行番号, 商品コード, 商品名, 数量, 単価, 金額
    /// </summary>
    public async Task<IEnumerable<string[]>> GetAllFlatAsync()
    {
        var rows = new List<string[]>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.sale_date, s.sale_no,
                   s.customer_code, s.customer_name,
                   s.line_no, s.product_code, s.product_name,
                   s.quantity, s.unit_price,
                   s.quantity * s.unit_price AS line_amount
            FROM   sales s
            WHERE  s.is_deleted = 0
            ORDER  BY s.sale_date DESC, s.sale_no, s.line_no";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new[]
            {
                DateOnly.FromDateTime(reader.GetDateTime(0)).ToString("yyyy/MM/dd"),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4).ToString(),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDecimal(7).ToString("#,##0.##"),
                reader.GetDecimal(8).ToString("#,##0"),
                reader.GetDecimal(9).ToString("#,##0"),
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
        cmd.CommandText = "usp_sales_summaries_select";
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

    public async Task<SaleSlip?> GetBySlipNoAsync(string saleNo)
    {
        SaleSlip? slip = null;
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // ── 伝票データ取得（usp_sales_select）──────────────────
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "usp_sales_select";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@sale_no", saleNo);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (slip is null)
                {
                    slip = new SaleSlip
                    {
                        SaleNo             = reader.GetString(reader.GetOrdinal("sale_no")),
                        SaleDate           = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("sale_date"))),
                        CustomerId         = reader.GetInt32(reader.GetOrdinal("customer_id")),
                        CustomerCode       = reader.GetString(reader.GetOrdinal("customer_code")),
                        CustomerName       = reader.GetString(reader.GetOrdinal("customer_name")),
                        CustomerPostalCode = reader.IsDBNull(reader.GetOrdinal("customer_postal_code")) ? null : reader.GetString(reader.GetOrdinal("customer_postal_code")),
                        CustomerAddress1   = reader.IsDBNull(reader.GetOrdinal("customer_address1"))    ? null : reader.GetString(reader.GetOrdinal("customer_address1")),
                        CustomerAddress2   = reader.IsDBNull(reader.GetOrdinal("customer_address2"))    ? null : reader.GetString(reader.GetOrdinal("customer_address2")),
                        OrderId            = reader.IsDBNull(reader.GetOrdinal("order_id"))   ? null : reader.GetInt32(reader.GetOrdinal("order_id")),
                        OrderNo            = reader.IsDBNull(reader.GetOrdinal("order_no"))   ? null : reader.GetString(reader.GetOrdinal("order_no")),
                        EmployeeId         = reader.GetInt32(reader.GetOrdinal("employee_id")),
                        EmployeeCode       = reader.GetString(reader.GetOrdinal("employee_code")),
                        EmployeeName       = reader.GetString(reader.GetOrdinal("employee_name")),
                        SlipRemarks        = reader.IsDBNull(reader.GetOrdinal("slip_remarks")) ? null : reader.GetString(reader.GetOrdinal("slip_remarks")),
                    };
                }

            slip.Lines.Add(new SaleLine
            {
                LineNo         = reader.GetInt32(reader.GetOrdinal("line_no")),
                ProductId      = reader.GetInt32(reader.GetOrdinal("product_id")),
                ProductCode    = reader.GetString(reader.GetOrdinal("product_code")),
                ProductName    = reader.GetString(reader.GetOrdinal("product_name")),
                Quantity       = reader.GetDecimal(reader.GetOrdinal("quantity")),
                UnitPrice      = reader.GetDecimal(reader.GetOrdinal("unit_price")),
                CostPrice      = reader.GetDecimal(reader.GetOrdinal("cost_price")),
                TaxTypeId      = reader.GetInt32(reader.GetOrdinal("tax_type_id")),
                TaxTypeName    = reader.GetString(reader.GetOrdinal("tax_type_name")),
                TaxRateType    = reader.GetByte(reader.GetOrdinal("tax_rate_type")),
                AppliedTaxRate = reader.IsDBNull(reader.GetOrdinal("applied_tax_rate")) ? 0m : reader.GetDecimal(reader.GetOrdinal("applied_tax_rate")),
                LineTaxAmount  = reader.IsDBNull(reader.GetOrdinal("line_tax_amount"))  ? 0m : reader.GetDecimal(reader.GetOrdinal("line_tax_amount")),
                LineRemarks    = reader.IsDBNull(reader.GetOrdinal("line_remarks"))      ? null : reader.GetString(reader.GetOrdinal("line_remarks")),
                });
            }
        } // end using cmd (reader closed)

        if (slip is null) return null;

        // ── ロック判定（invoiced_at または ar_aggregated_at が設定済みか）──
        await using (var lockCmd = conn.CreateCommand())
        {
            lockCmd.CommandText = """
                SELECT COUNT(1) FROM sales
                WHERE sale_no    = @sale_no
                  AND is_deleted = 0
                  AND (invoiced_at IS NOT NULL OR ar_aggregated_at IS NOT NULL)
                """;
            lockCmd.Parameters.AddWithValue("@sale_no", saleNo);
            var count = (int)(await lockCmd.ExecuteScalarAsync())!;
            slip.IsLocked = count > 0;
        }

        return slip;
    }

    public async Task UpsertAsync(
        string saleNo, DateOnly saleDate, int customerId,
        int? orderId, string? orderNo, int employeeId,
        string? slipRemarks, IEnumerable<SaleLineInput> lines)
    {
        var linesJson = JsonSerializer.Serialize(lines.Select(l => new
        {
            line_no          = l.LineNo,
            product_id       = l.ProductId,
            product_code     = l.ProductCode,
            product_name     = l.ProductName,
            quantity         = l.Quantity,
            unit_price       = l.UnitPrice,
            cost_price       = l.CostPrice,
            tax_type_id      = l.TaxTypeId,
            tax_rate_type    = l.TaxRateType,
            applied_tax_rate = l.AppliedTaxRate,
            line_tax_amount  = l.LineTaxAmount,
            slip_tax_amount  = l.SlipTaxAmount,
            line_remarks     = l.LineRemarks,
        }));

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_sales_upsert";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@sale_no",       saleNo);
        cmd.Parameters.AddWithValue("@sale_date",     saleDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@customer_id",   customerId);
        cmd.Parameters.AddWithValue("@order_id",      (object?)orderId   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@order_no",      (object?)orderNo   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@employee_id",   employeeId);
        cmd.Parameters.AddWithValue("@slip_remarks",  (object?)slipRemarks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lines",         linesJson);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string saleNo)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_sales_delete";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@sale_no", saleNo);
        await cmd.ExecuteNonQueryAsync();
    }
}
