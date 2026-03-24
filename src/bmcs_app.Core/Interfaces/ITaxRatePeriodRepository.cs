using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface ITaxRatePeriodRepository
{
    Task<IEnumerable<TaxRatePeriod>> GetAllAsync();
    Task UpsertAsync(int? taxRatePeriodId, DateOnly startDate, DateOnly? endDate,
                     decimal primaryTaxRate, decimal secondaryTaxRate, decimal? tertiaryTaxRate);
    Task DeleteAsync(int taxRatePeriodId);
}
