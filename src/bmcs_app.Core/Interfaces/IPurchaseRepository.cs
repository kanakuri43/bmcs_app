using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface IPurchaseRepository
{
    Task<IEnumerable<SlipSummary>>  GetSummariesAsync();
    Task<PurchaseSlip?>             GetByPurchaseNoAsync(string purchaseNo);
    Task UpsertAsync(string purchaseNo, DateOnly purchaseDate, int supplierId,
        int? purchaseOrderId, string? purchaseOrderNo, int employeeId,
        string? slipRemarks, IEnumerable<PurchaseLineInput> lines);
    Task DeleteAsync(string purchaseNo);
}
