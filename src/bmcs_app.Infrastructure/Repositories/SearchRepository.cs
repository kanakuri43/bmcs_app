using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class SearchRepository : ISearchRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task<IEnumerable<SearchResultItem>> SearchAsync(
        bool      includeOrders,
        bool      includeSales,
        bool      includeReceipts,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string?   keyword,
        string?   customerCode,
        string    aggregationStatus)
    {
        var parts = new List<string>();

        if (includeOrders)
            parts.Add(BuildOrdersQuery(dateFrom, dateTo, keyword, customerCode, aggregationStatus));

        if (includeSales)
            parts.Add(BuildSalesQuery(dateFrom, dateTo, keyword, customerCode, aggregationStatus));

        if (includeReceipts)
            parts.Add(BuildReceiptsQuery(dateFrom, dateTo, keyword, customerCode, aggregationStatus));

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
        if (!string.IsNullOrWhiteSpace(customerCode))
            cmd.Parameters.AddWithValue("@customer_code", customerCode);

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

    private static string BuildOrdersQuery(
        DateOnly? dateFrom, DateOnly? dateTo,
        string? keyword, string? customerCode, string aggregationStatus)
    {
        var where = new List<string> { "o.is_deleted = 0" };
        if (dateFrom.HasValue)                        where.Add("o.order_date >= @date_from");
        if (dateTo.HasValue)                          where.Add("o.order_date <= @date_to");
        if (!string.IsNullOrWhiteSpace(keyword))      where.Add("(o.customer_name LIKE @keyword OR o.product_name LIKE @keyword OR o.slip_remarks LIKE @keyword OR o.line_remarks LIKE @keyword)");
        if (!string.IsNullOrWhiteSpace(customerCode)) where.Add("o.customer_code = @customer_code");
        if (aggregationStatus == "unprocessed")       where.Add("NOT EXISTS (SELECT 1 FROM sales s2 WHERE s2.sale_no = o.order_no AND s2.is_deleted = 0)");
        else if (aggregationStatus == "processed")    where.Add("EXISTS (SELECT 1 FROM sales s2 WHERE s2.sale_no = o.order_no AND s2.is_deleted = 0)");

        return $"""
            SELECT N'受注' AS slip_type, o.order_no AS slip_no,
                   MIN(o.order_date) AS slip_date, MAX(o.customer_name) AS customer_name,
                   SUM(o.quantity * o.unit_price) AS amount,
                   MAX(CASE WHEN s.sale_no IS NOT NULL THEN N'売上済' ELSE N'未処理' END) AS status,
                   MAX(ISNULL(o.slip_remarks, N'')) AS remarks
            FROM orders o
            LEFT JOIN (SELECT DISTINCT sale_no FROM sales WHERE is_deleted = 0) s ON s.sale_no = o.order_no
            WHERE {string.Join(" AND ", where)}
            GROUP BY o.order_no
            """;
    }

    private static string BuildSalesQuery(
        DateOnly? dateFrom, DateOnly? dateTo,
        string? keyword, string? customerCode, string aggregationStatus)
    {
        var where = new List<string> { "is_deleted = 0" };
        if (dateFrom.HasValue)                        where.Add("sale_date >= @date_from");
        if (dateTo.HasValue)                          where.Add("sale_date <= @date_to");
        if (!string.IsNullOrWhiteSpace(keyword))      where.Add("(customer_name LIKE @keyword OR product_name LIKE @keyword OR slip_remarks LIKE @keyword OR line_remarks LIKE @keyword)");
        if (!string.IsNullOrWhiteSpace(customerCode)) where.Add("customer_code = @customer_code");
        if (aggregationStatus == "unprocessed")       where.Add("invoiced_at IS NULL AND ar_aggregated_at IS NULL");
        else if (aggregationStatus == "processed")    where.Add("(invoiced_at IS NOT NULL OR ar_aggregated_at IS NOT NULL)");

        return $"""
            SELECT N'売上' AS slip_type, sale_no AS slip_no,
                   MIN(sale_date) AS slip_date, MAX(customer_name) AS customer_name,
                   SUM(quantity * unit_price) AS amount,
                   MAX(CASE
                       WHEN invoiced_at IS NOT NULL AND ar_aggregated_at IS NOT NULL THEN N'請求・売掛済'
                       WHEN invoiced_at IS NOT NULL  THEN N'請求済'
                       WHEN ar_aggregated_at IS NOT NULL THEN N'売掛済'
                       ELSE N'未処理'
                   END) AS status,
                   MAX(ISNULL(slip_remarks, N'')) AS remarks
            FROM sales
            WHERE {string.Join(" AND ", where)}
            GROUP BY sale_no
            """;
    }

    private static string BuildReceiptsQuery(
        DateOnly? dateFrom, DateOnly? dateTo,
        string? keyword, string? customerCode, string aggregationStatus)
    {
        var where = new List<string> { "is_deleted = 0" };
        if (dateFrom.HasValue)                        where.Add("receipt_date >= @date_from");
        if (dateTo.HasValue)                          where.Add("receipt_date <= @date_to");
        if (!string.IsNullOrWhiteSpace(keyword))      where.Add("(customer_name LIKE @keyword OR slip_remarks LIKE @keyword OR line_remarks LIKE @keyword)");
        if (!string.IsNullOrWhiteSpace(customerCode)) where.Add("customer_code = @customer_code");
        if (aggregationStatus == "unprocessed")       where.Add("invoiced_at IS NULL AND ar_aggregated_at IS NULL");
        else if (aggregationStatus == "processed")    where.Add("(invoiced_at IS NOT NULL OR ar_aggregated_at IS NOT NULL)");

        return $"""
            SELECT N'入金' AS slip_type, receipt_no AS slip_no,
                   MIN(receipt_date) AS slip_date, MAX(customer_name) AS customer_name,
                   SUM(amount) AS amount,
                   MAX(CASE
                       WHEN invoiced_at IS NOT NULL AND ar_aggregated_at IS NOT NULL THEN N'請求・集計済'
                       WHEN invoiced_at IS NOT NULL  THEN N'請求済'
                       WHEN ar_aggregated_at IS NOT NULL THEN N'集計済'
                       ELSE N'未処理'
                   END) AS status,
                   MAX(ISNULL(slip_remarks, N'')) AS remarks
            FROM receipts
            WHERE {string.Join(" AND ", where)}
            GROUP BY receipt_no
            """;
    }
}
