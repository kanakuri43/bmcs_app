using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface ISearchRepository
{
    Task<IEnumerable<SearchResultItem>> SearchAsync(
        bool      includeSales,
        bool      includeReceipts,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string?   keyword,
        string?   customerCode,
        string    aggregationStatus);   // "all" | "unprocessed" | "processed"
}
