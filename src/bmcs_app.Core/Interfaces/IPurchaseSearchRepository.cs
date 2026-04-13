using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface IPurchaseSearchRepository
{
    Task<IEnumerable<SearchResultItem>> SearchAsync(
        bool      includePurchaseOrders,
        bool      includePurchases,
        bool      includePayments,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string?   keyword,
        string?   supplierCode,
        string    aggregationStatus);   // "all" | "unprocessed" | "processed"
}
