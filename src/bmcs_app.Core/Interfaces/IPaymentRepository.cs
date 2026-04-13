using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface IPaymentRepository
{
    Task<IEnumerable<SlipSummary>>  GetSummariesAsync();
    Task<PaymentSlip?>              GetByPaymentNoAsync(string paymentNo);
    Task UpsertAsync(string paymentNo, DateOnly paymentDate, int supplierId,
        string? slipRemarks, IEnumerable<PaymentLineInput> lines);
    Task DeleteAsync(string paymentNo);
}
