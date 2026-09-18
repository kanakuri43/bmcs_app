using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>入金方法マスタのユースケース。</summary>
public class DepositMethodService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<DepositMethodService> logger)
{
    public Task<List<DepositMethod>> GetDepositMethodsAsync(CancellationToken cancellationToken = default)
        => dbContext.DepositMethods
            .AsNoTracking()
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.DisplayOrder)
            .ThenBy(m => m.DepositMethodCode)
            .ToListAsync(cancellationToken);

    public Task<DepositMethod?> GetByCodeAsync(string depositMethodCode, CancellationToken cancellationToken = default)
        => dbContext.DepositMethods
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.DepositMethodCode == depositMethodCode, cancellationToken);

    public async Task<DepositMethod> CreateAsync(DepositMethod depositMethod, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.DepositMethods
            .AsNoTracking()
            .AnyAsync(m => m.DepositMethodCode == depositMethod.DepositMethodCode, cancellationToken);
        if (exists)
        {
            throw new DepositMethodValidationException($"入金方法コード「{depositMethod.DepositMethodCode}」は既に登録されています。");
        }

        ValidateRequiresFlags(depositMethod);

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        depositMethod.CreatedBy = employeeCode;
        depositMethod.CreatedAt = now;
        depositMethod.UpdatedBy = employeeCode;
        depositMethod.UpdatedAt = now;
        depositMethod.IsDeleted = false;

        dbContext.DepositMethods.Add(depositMethod);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("入金方法を登録しました。DepositMethodCode={DepositMethodCode}", depositMethod.DepositMethodCode);
        return depositMethod;
    }

    public async Task<DepositMethod> UpdateAsync(DepositMethod depositMethod, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.DepositMethods
            .SingleOrDefaultAsync(m => m.DepositMethodCode == depositMethod.DepositMethodCode, cancellationToken)
            ?? throw new DepositMethodValidationException($"入金方法コード「{depositMethod.DepositMethodCode}」は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(depositMethod.RowVersion!))
        {
            throw new DepositMethodConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        ValidateRequiresFlags(depositMethod);

        current.DepositMethodName = depositMethod.DepositMethodName;
        current.RequiresBankAccount = depositMethod.RequiresBankAccount;
        current.RequiresBillDueDate = depositMethod.RequiresBillDueDate;
        current.DisplayOrder = depositMethod.DisplayOrder;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new DepositMethodConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("入金方法を更新しました。DepositMethodCode={DepositMethodCode}", depositMethod.DepositMethodCode);
        return current;
    }

    public async Task DeactivateAsync(DepositMethod depositMethod, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.DepositMethods
            .SingleOrDefaultAsync(m => m.DepositMethodCode == depositMethod.DepositMethodCode, cancellationToken)
            ?? throw new DepositMethodValidationException($"入金方法コード「{depositMethod.DepositMethodCode}」は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(depositMethod.RowVersion!))
        {
            throw new DepositMethodConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
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
            throw new DepositMethodConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("入金方法を無効化しました。DepositMethodCode={DepositMethodCode}", depositMethod.DepositMethodCode);
    }

    /// <summary>口座と手形期日を同時に必須にすることはできない（CK_deposit_method_requiresの先回り）。</summary>
    private static void ValidateRequiresFlags(DepositMethod depositMethod)
    {
        if (depositMethod.RequiresBankAccount && depositMethod.RequiresBillDueDate)
        {
            throw new DepositMethodValidationException("入金先口座と手形期日の両方を必須にすることはできません。");
        }
    }
}

/// <summary>入金方法マスタの業務ルール違反（形式チェック済みの入力に対する業務的な拒否）。</summary>
public sealed class DepositMethodValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class DepositMethodConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
