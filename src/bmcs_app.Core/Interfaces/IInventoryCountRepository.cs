using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface IInventoryCountRepository
{
    Task<IEnumerable<DateOnly>> GetAllDatesAsync();
    Task<IEnumerable<InventoryCountLine>> GetByDateAsync(DateOnly date);
    Task UpsertAsync(DateOnly date, IEnumerable<InventoryCountLineInput> lines);
    Task DeleteByDateAsync(DateOnly date);
}
