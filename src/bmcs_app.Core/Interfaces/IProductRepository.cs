using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface IProductRepository
{
    Task<IEnumerable<Product>> GetAllAsync();
    Task UpsertAsync(int? productId, string code, string name, int taxTypeId, byte taxRateType, decimal costPrice, bool isMiscellaneous = false);
    Task DeleteAsync(int productId);
}
