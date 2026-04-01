using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface IProductRepository
{
    Task<IEnumerable<Product>> GetAllAsync();
}
