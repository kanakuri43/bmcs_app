using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface ISupplierRepository
{
    Task<IEnumerable<Supplier>>                  GetAllAsync();
    Task<IEnumerable<TaxFractionClassification>> GetTaxFractionsAsync();
    Task<IEnumerable<TaxCalcUnitClassification>> GetTaxCalcUnitsAsync();
    Task<IEnumerable<Employee>>                  GetEmployeesAsync();
    Task UpsertAsync(int? supplierId, string supplierCode, string supplierName,
        byte closingDay, int taxFractionId, int taxCalcUnitId, int? employeeId,
        string? postalCode, string? address1, string? address2);
    Task DeleteAsync(int supplierId);
}
