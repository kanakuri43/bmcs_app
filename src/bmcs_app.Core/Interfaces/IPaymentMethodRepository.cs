using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface IPaymentMethodRepository
{
    Task<IEnumerable<PaymentMethod>> GetAllAsync();
}
