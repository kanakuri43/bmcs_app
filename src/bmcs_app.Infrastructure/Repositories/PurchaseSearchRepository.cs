using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class PurchaseSearchRepository : IPurchaseSearchRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task<IEnumerable<SearchResultItem>> SearchAsync(
        bool      includePurchaseOrders,
        bool      includePurchases,
        bool      includePayments,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string?   keyword,
        string?   supplierCode,
        string    aggregationStatus)
    {
        var parts = new List<string>();

        if (includePurchaseOrders)
            parts.Add(BuildPurchaseOrdersQuery(dateFrom, dateTo, keyword, supplierCode, aggregationStatus));

        if (includePurchases)
            parts.Add(BuildPurchasesQuery(dateFrom, dateTo, keyword, supplierCode, aggregationStatus));

        if (includePayments)
            parts.Add(BuildPaymentsQuery(dateFrom, dateTo, keyword, supplierCode, aggregationStatus));

        if (parts.Count == 0) return [];

        var sql = string.Join("\nUNION ALL\n", parts)
                + "\nORDER BY slip_date DESC, slip_no";

        var list = new List<SearchResultItem>();
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        if (dateFrom.HasValue)
            cmd.Parameters.AddWithValue("@date_from", dateFrom.Value.ToDateTime(TimeOnly.MinValue));
        if (dateTo.HasValue)
            cmd.Parameters.AddWithValue("@date_to", dateTo.Value.ToDateTime(TimeOnly.MinValue));
        if (!string.IsNullOrWhiteSpace(keyword))
            cmd.Parameters.AddWithValue("@keyword", $"%{keyword}%");
        if (!string.IsNullOrWhiteSpace(supplierCode))
            cmd.Parameters.AddWithValue("@supplier_code", supplierCode);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new SearchResultItem
            {
                SlipType     = reader.GetString(0),
                SlipNo       = reader.GetString(1),
                SlipDate     = reader.GetDateTime(2),
                CustomerName = reader.GetString(3),
                Amount       = reader.GetDecimal(4),
                Status       = reader.GetString(5),
                Remarks      = reader.GetString(6),
            });
        }
        return list;
    }

    private static string BuildPurchaseOrdersQuery(
        DateOnly? dateFrom, DateOnly? dateTo,
        string? keyword, string? supplierCode, string aggregationStatus)
    {
        var where = new List<string> { "po.is_deleted = 0" };
        if (dateFrom.HasValue)                        where.Add("po.purchase_order_date >= @date_from");
        if (dateTo.HasValue)                          where.Add("po.purchase_order_date <= @date_to");
        if (!string.IsNullOrWhiteSpace(keyword))      where.Add("(po.supplier_name LIKE @keyword OR po.product_name LIKE @keyword OR po.slip_remarks LIKE @keyword OR po.line_remarks LIKE @keyword)");
        if (!string.IsNullOrWhiteSpace(supplierCode)) where.Add("po.supplier_code = @supplier_code");
        if (aggregationStatus == "unprocessed")       where.Add("NOT EXISTS (SELECT 1 FROM purchases p2 WHERE p2.purchase_order_no = po.purchase_order_no AND p2.is_deleted = 0)");
        else if (aggregationStatus == "processed")    where.Add("EXISTS (SELECT 1 FROM purchases p2 WHERE p2.purchase_order_no = po.purchase_order_no AND p2.is_deleted = 0)");

        return $"""
            SELECT N'発注' AS slip_type, po.purchase_order_no AS slip_no,
                   MIN(po.purchase_order_date) AS slip_date, MAX(po.supplier_name) AS customer_name,
                   SUM(po.quantity * po.unit_price) AS amount,
                   MAX(CASE WHEN p.purchase_order_no IS NOT NULL THEN N'仕入済' ELSE N'未処理' END) AS status,
                   MAX(ISNULL(po.slip_remarks, N'')) AS remarks
            FROM purchase_orders po
            LEFT JOIN (SELECT DISTINCT purchase_order_no FROM purchases WHERE is_deleted = 0) p
                   ON p.purchase_order_no = po.purchase_order_no
            WHERE {string.Join(" AND ", where)}
            GROUP BY po.purchase_order_no
            """;
    }

    private static string BuildPurchasesQuery(
        DateOnly? dateFrom, DateOnly? dateTo,
        string? keyword, string? supplierCode, string aggregationStatus)
    {
        var where = new List<string> { "is_deleted = 0" };
        if (dateFrom.HasValue)                        where.Add("purchase_date >= @date_from");
        if (dateTo.HasValue)                          where.Add("purchase_date <= @date_to");
        if (!string.IsNullOrWhiteSpace(keyword))      where.Add("(supplier_name LIKE @keyword OR product_name LIKE @keyword OR slip_remarks LIKE @keyword OR line_remarks LIKE @keyword)");
        if (!string.IsNullOrWhiteSpace(supplierCode)) where.Add("supplier_code = @supplier_code");
        if (aggregationStatus == "unprocessed")       where.Add("ap_closing_at IS NULL");
        else if (aggregationStatus == "processed")    where.Add("ap_closing_at IS NOT NULL");

        return $"""
            SELECT N'仕入' AS slip_type, purchase_no AS slip_no,
                   MIN(purchase_date) AS slip_date, MAX(supplier_name) AS customer_name,
                   SUM(quantity * unit_price) AS amount,
                   MAX(CASE WHEN ap_closing_at IS NOT NULL THEN N'締済' ELSE N'未締' END) AS status,
                   MAX(ISNULL(slip_remarks, N'')) AS remarks
            FROM purchases
            WHERE {string.Join(" AND ", where)}
            GROUP BY purchase_no
            """;
    }

    private static string BuildPaymentsQuery(
        DateOnly? dateFrom, DateOnly? dateTo,
        string? keyword, string? supplierCode, string aggregationStatus)
    {
        var where = new List<string> { "is_deleted = 0" };
        if (dateFrom.HasValue)                        where.Add("payment_date >= @date_from");
        if (dateTo.HasValue)                          where.Add("payment_date <= @date_to");
        if (!string.IsNullOrWhiteSpace(keyword))      where.Add("(supplier_name LIKE @keyword OR slip_remarks LIKE @keyword OR line_remarks LIKE @keyword)");
        if (!string.IsNullOrWhiteSpace(supplierCode)) where.Add("supplier_code = @supplier_code");
        if (aggregationStatus == "unprocessed")       where.Add("ap_closing_at IS NULL");
        else if (aggregationStatus == "processed")    where.Add("ap_closing_at IS NOT NULL");

        return $"""
            SELECT N'支払' AS slip_type, payment_no AS slip_no,
                   MIN(payment_date) AS slip_date, MAX(supplier_name) AS customer_name,
                   SUM(amount) AS amount,
                   MAX(CASE WHEN ap_closing_at IS NOT NULL THEN N'締済' ELSE N'未締' END) AS status,
                   MAX(ISNULL(slip_remarks, N'')) AS remarks
            FROM payments
            WHERE {string.Join(" AND ", where)}
            GROUP BY payment_no
            """;
    }
}
