using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface ICustomerRepository
{
    Task<IEnumerable<Customer>>                  GetAllAsync();
    Task<IEnumerable<TaxFractionClassification>> GetTaxFractionsAsync();
    Task<IEnumerable<TaxCalcUnitClassification>> GetTaxCalcUnitsAsync();
    Task<IEnumerable<Employee>>                  GetEmployeesAsync();
    Task UpsertAsync(int? customerId, string code, string name,
                     byte closingDay, int taxFractionId, int taxCalcUnitId,
                     int? employeeId,
                     string? postalCode, string? address1, string? address2);
    Task DeleteAsync(int customerId);
}
