using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>コピー機マスタ（機番→得意先の変換マスタ）のユースケース。</summary>
public class CopierMachineService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<CopierMachineService> logger)
{
    public Task<List<CopierMachine>> GetCopierMachinesAsync(CancellationToken cancellationToken = default)
        => dbContext.CopierMachines
            .AsNoTracking()
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.MachineNo)
            .ToListAsync(cancellationToken);

    public Task<CopierMachine?> GetByMachineNoAsync(string machineNo, CancellationToken cancellationToken = default)
        => dbContext.CopierMachines
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.MachineNo == machineNo, cancellationToken);

    public async Task<CopierMachine> CreateAsync(CopierMachine machine, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.CopierMachines
            .AsNoTracking()
            .AnyAsync(m => m.MachineNo == machine.MachineNo, cancellationToken);
        if (exists)
        {
            throw new CopierMachineValidationException($"機番「{machine.MachineNo}」は既に登録されています。");
        }

        await EnsureActiveCustomerAsync(machine.CustomerCode, cancellationToken);

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        machine.CreatedBy = employeeCode;
        machine.CreatedAt = now;
        machine.UpdatedBy = employeeCode;
        machine.UpdatedAt = now;
        machine.IsDeleted = false;

        dbContext.CopierMachines.Add(machine);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("コピー機を登録しました。MachineNo={MachineNo}", machine.MachineNo);
        return machine;
    }

    public async Task<CopierMachine> UpdateAsync(CopierMachine machine, CancellationToken cancellationToken = default)
    {
        var current = await LoadForUpdateAsync(machine, cancellationToken);

        if (current.CustomerCode != machine.CustomerCode)
        {
            await EnsureActiveCustomerAsync(machine.CustomerCode, cancellationToken);
        }

        current.CustomerCode = machine.CustomerCode;
        current.MachineModel = machine.MachineModel;
        current.Remarks = machine.Remarks;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        await SaveAsync(cancellationToken);

        logger.LogInformation("コピー機を更新しました。MachineNo={MachineNo}", machine.MachineNo);
        return current;
    }

    public async Task DeactivateAsync(CopierMachine machine, CancellationToken cancellationToken = default)
    {
        var current = await LoadForUpdateAsync(machine, cancellationToken);

        current.IsDeleted = true;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        await SaveAsync(cancellationToken);

        logger.LogInformation("コピー機を無効化しました。MachineNo={MachineNo}", machine.MachineNo);
    }

    private async Task<CopierMachine> LoadForUpdateAsync(CopierMachine machine, CancellationToken cancellationToken)
    {
        var current = await dbContext.CopierMachines
            .SingleOrDefaultAsync(m => m.MachineNo == machine.MachineNo, cancellationToken)
            ?? throw new CopierMachineValidationException($"機番「{machine.MachineNo}」は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(machine.RowVersion!))
        {
            throw new CopierMachineConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        return current;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new CopierMachineConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }
    }

    private async Task EnsureActiveCustomerAsync(string customerCode, CancellationToken cancellationToken)
    {
        var isActive = await dbContext.Customers
            .AsNoTracking()
            .AnyAsync(c => c.CustomerCode == customerCode && !c.IsDeleted, cancellationToken);
        if (!isActive)
        {
            throw new CopierMachineValidationException($"得意先コード「{customerCode}」は登録されていないか、無効です。");
        }
    }
}

/// <summary>コピー機マスタの業務ルール違反（形式チェック済みの入力に対する業務的な拒否）。</summary>
public sealed class CopierMachineValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class CopierMachineConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
