using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface IPurchaseOrderRepository
{
    Task<IEnumerable<string[]>>        GetAllFlatAsync();
    Task<IEnumerable<SlipSummary>>     GetSummariesAsync();
    Task<PurchaseOrderSlip?>           GetByPurchaseOrderNoAsync(string purchaseOrderNo);
    Task UpsertAsync(string purchaseOrderNo, DateOnly purchaseOrderDate, int supplierId, int employeeId,
        string? slipRemarks, IEnumerable<PurchaseOrderLineInput> lines);
    Task DeleteAsync(string purchaseOrderNo);
}
