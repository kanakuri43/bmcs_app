using System.Data;
using System.Text.Json;
using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class PurchaseRepository : IPurchaseRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    /// <summary>
    /// 伝票検索ダイアログ用。仕入明細を非正規化した全行を返す（直接 SQL）。
    /// 列順: 仕入日付, 仕入番号, 仕入先コード, 仕入先名, 行番号, 商品コード, 商品名, 数量, 単価, 金額
    /// </summary>
    public async Task<IEnumerable<string[]>> GetAllFlatAsync()
    {
        var rows = new List<string[]>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT purchase_date, purchase_no,
                   supplier_code, supplier_name,
                   line_no, product_code, product_name,
                   quantity, unit_price,
                   quantity * unit_price AS line_amount
            FROM   purchases
            WHERE  is_deleted = 0
            ORDER  BY purchase_date DESC, purchase_no, line_no";
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
        cmd.CommandText = "usp_purchases_summaries_select";
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

    public async Task<PurchaseSlip?> GetByPurchaseNoAsync(string purchaseNo)
    {
        PurchaseSlip? slip = null;
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "usp_purchases_select";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@purchase_no", purchaseNo);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (slip is null)
                {
                    var apClosingAt = reader.IsDBNull(reader.GetOrdinal("ap_closing_at"))
                        ? (DateOnly?)null
                        : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("ap_closing_at")));

                    slip = new PurchaseSlip
                    {
                        PurchaseNo         = reader.GetString(reader.GetOrdinal("purchase_no")),
                        PurchaseDate       = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("purchase_date"))),
                        SupplierId         = reader.GetInt32(reader.GetOrdinal("supplier_id")),
                        SupplierCode       = reader.GetString(reader.GetOrdinal("supplier_code")),
                        SupplierName       = reader.GetString(reader.GetOrdinal("supplier_name")),
                        SupplierPostalCode = reader.IsDBNull(reader.GetOrdinal("supplier_postal_code")) ? null : reader.GetString(reader.GetOrdinal("supplier_postal_code")),
                        SupplierAddress1   = reader.IsDBNull(reader.GetOrdinal("supplier_address1"))    ? null : reader.GetString(reader.GetOrdinal("supplier_address1")),
                        SupplierAddress2   = reader.IsDBNull(reader.GetOrdinal("supplier_address2"))    ? null : reader.GetString(reader.GetOrdinal("supplier_address2")),
                        PurchaseOrderId    = reader.IsDBNull(reader.GetOrdinal("purchase_order_id"))    ? null : reader.GetInt32(reader.GetOrdinal("purchase_order_id")),
                        PurchaseOrderNo    = reader.IsDBNull(reader.GetOrdinal("purchase_order_no"))    ? null : reader.GetString(reader.GetOrdinal("purchase_order_no")),
                        EmployeeId         = reader.GetInt32(reader.GetOrdinal("employee_id")),
                        EmployeeCode       = reader.GetString(reader.GetOrdinal("employee_code")),
                        EmployeeName       = reader.GetString(reader.GetOrdinal("employee_name")),
                        SlipRemarks        = reader.IsDBNull(reader.GetOrdinal("slip_remarks")) ? null : reader.GetString(reader.GetOrdinal("slip_remarks")),
                        ApClosingAt        = apClosingAt,
                        IsLocked           = apClosingAt.HasValue,
                    };
                }

                slip.Lines.Add(new PurchaseLine
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
        }

        return slip;
    }

    public async Task UpsertAsync(
        string purchaseNo, DateOnly purchaseDate, int supplierId,
        int? purchaseOrderId, string? purchaseOrderNo, int employeeId,
        string? slipRemarks, IEnumerable<PurchaseLineInput> lines)
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
        cmd.CommandText = "usp_purchases_upsert";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@purchase_no",        purchaseNo);
        cmd.Parameters.AddWithValue("@purchase_date",      purchaseDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@supplier_id",        supplierId);
        cmd.Parameters.AddWithValue("@purchase_order_id",  (object?)purchaseOrderId  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@purchase_order_no",  (object?)purchaseOrderNo  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@employee_id",        employeeId);
        cmd.Parameters.AddWithValue("@slip_remarks",       (object?)slipRemarks      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lines",              linesJson);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string purchaseNo)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "usp_purchases_delete";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@purchase_no", purchaseNo);
        await cmd.ExecuteNonQueryAsync();
    }
}
