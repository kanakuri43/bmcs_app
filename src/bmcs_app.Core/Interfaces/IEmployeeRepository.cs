using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface IEmployeeRepository
{
    Task<IEnumerable<Employee>> GetAllAsync();
    Task UpsertAsync(int? employeeId, string code, string name);
    Task DeleteAsync(int employeeId);
}
