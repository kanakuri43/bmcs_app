using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>社員マスタのユースケース。</summary>
public class EmployeeService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<EmployeeService> logger)
{
    public Task<List<Employee>> GetEmployeesAsync(CancellationToken cancellationToken = default)
        => dbContext.Employees
            .AsNoTracking()
            .Where(e => !e.IsDeleted)
            .OrderBy(e => e.EmployeeCode)
            .ToListAsync(cancellationToken);

    public Task<Employee?> GetByCodeAsync(string employeeCode, CancellationToken cancellationToken = default)
        => dbContext.Employees
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.EmployeeCode == employeeCode, cancellationToken);

    /// <summary>有効（未無効化）な社員だけを返す。伝票の担当者を新たに入力するときに使う。</summary>
    public Task<Employee?> GetActiveByCodeAsync(string employeeCode, CancellationToken cancellationToken = default)
        => dbContext.Employees
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.EmployeeCode == employeeCode && !e.IsDeleted, cancellationToken);

    public async Task<Employee> CreateAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.Employees
            .AsNoTracking()
            .AnyAsync(e => e.EmployeeCode == employee.EmployeeCode, cancellationToken);
        if (exists)
        {
            throw new EmployeeValidationException($"社員コード「{employee.EmployeeCode}」は既に登録されています。");
        }

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        employee.CreatedBy = employeeCode;
        employee.CreatedAt = now;
        employee.UpdatedBy = employeeCode;
        employee.UpdatedAt = now;
        employee.IsDeleted = false;

        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("社員を登録しました。EmployeeCode={EmployeeCode}", employee.EmployeeCode);
        return employee;
    }

    public async Task<Employee> UpdateAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.Employees
            .SingleOrDefaultAsync(e => e.EmployeeCode == employee.EmployeeCode, cancellationToken)
            ?? throw new EmployeeValidationException($"社員コード「{employee.EmployeeCode}」は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(employee.RowVersion!))
        {
            throw new EmployeeConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        current.EmployeeName = employee.EmployeeName;
        current.EmployeeNameKana = employee.EmployeeNameKana;
        current.PermissionLevel = employee.PermissionLevel;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new EmployeeConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("社員を更新しました。EmployeeCode={EmployeeCode}", employee.EmployeeCode);
        return current;
    }

    public async Task DeactivateAsync(Employee employee, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.Employees
            .SingleOrDefaultAsync(e => e.EmployeeCode == employee.EmployeeCode, cancellationToken)
            ?? throw new EmployeeValidationException($"社員コード「{employee.EmployeeCode}」は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(employee.RowVersion!))
        {
            throw new EmployeeConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        current.IsDeleted = true;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new EmployeeConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("社員を無効化しました。EmployeeCode={EmployeeCode}", employee.EmployeeCode);
    }
}

/// <summary>社員マスタの業務ルール違反（形式チェック済みの入力に対する業務的な拒否）。</summary>
public sealed class EmployeeValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class EmployeeConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
