using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface IOrderRepository
{
    Task<IEnumerable<string[]>>   GetAllFlatAsync();
    Task<IEnumerable<SlipSummary>> GetSummariesAsync();
    Task<OrderSlip?>               GetByOrderNoAsync(string orderNo);
    Task UpsertAsync(string orderNo, DateOnly orderDate, int customerId, int employeeId,
        string? slipRemarks, IEnumerable<OrderLineInput> lines);
    Task DeleteAsync(string orderNo);
}
