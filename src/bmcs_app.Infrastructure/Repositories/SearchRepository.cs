using bmcs_app.Core.Interfaces;
using bmcs_app.Core.Models;
using Microsoft.Data.SqlClient;

namespace bmcs_app.Infrastructure.Repositories;

public class SearchRepository : ISearchRepository
{
    private static string ConnectionString => AppConfig.ConnectionString;

    public async Task<IEnumerable<SearchResultItem>> SearchAsync(
        bool      includeSales,
        bool      includeReceipts,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string?   keyword,
        string?   customerCode,
        string    aggregationStatus)
    {
        var parts = new List<string>();

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
        if (aggregationStatus == "unprocessed")       where.Add("ar_aggregated_at IS NULL");
        else if (aggregationStatus == "processed")    where.Add("ar_aggregated_at IS NOT NULL");

        return $"""
            SELECT N'入金' AS slip_type, receipt_no AS slip_no,
                   MIN(receipt_date) AS slip_date, MAX(customer_name) AS customer_name,
                   SUM(amount) AS amount,
                   MAX(CASE
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
