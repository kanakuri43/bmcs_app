using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>銀行口座マスタのユースケース。</summary>
public class BankAccountService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<BankAccountService> logger)
{
    public Task<List<BankAccount>> GetBankAccountsAsync(CancellationToken cancellationToken = default)
        => dbContext.BankAccounts
            .AsNoTracking()
            .Where(b => !b.IsDeleted)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.BankAccountCode)
            .ToListAsync(cancellationToken);

    public Task<BankAccount?> GetByCodeAsync(string bankAccountCode, CancellationToken cancellationToken = default)
        => dbContext.BankAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.BankAccountCode == bankAccountCode, cancellationToken);

    public async Task<BankAccount> CreateAsync(BankAccount bankAccount, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.BankAccounts
            .AsNoTracking()
            .AnyAsync(b => b.BankAccountCode == bankAccount.BankAccountCode, cancellationToken);
        if (exists)
        {
            throw new BankAccountValidationException($"銀行口座コード「{bankAccount.BankAccountCode}」は既に登録されています。");
        }

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        bankAccount.CreatedBy = employeeCode;
        bankAccount.CreatedAt = now;
        bankAccount.UpdatedBy = employeeCode;
        bankAccount.UpdatedAt = now;
        bankAccount.IsDeleted = false;

        dbContext.BankAccounts.Add(bankAccount);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("銀行口座を登録しました。BankAccountCode={BankAccountCode}", bankAccount.BankAccountCode);
        return bankAccount;
    }

    public async Task<BankAccount> UpdateAsync(BankAccount bankAccount, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.BankAccounts
            .SingleOrDefaultAsync(b => b.BankAccountCode == bankAccount.BankAccountCode, cancellationToken)
            ?? throw new BankAccountValidationException($"銀行口座コード「{bankAccount.BankAccountCode}」は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(bankAccount.RowVersion!))
        {
            throw new BankAccountConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        current.BankName = bankAccount.BankName;
        current.BranchName = bankAccount.BranchName;
        current.AccountType = bankAccount.AccountType;
        current.AccountNumber = bankAccount.AccountNumber;
        current.AccountHolderName = bankAccount.AccountHolderName;
        current.DisplayOrder = bankAccount.DisplayOrder;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new BankAccountConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("銀行口座を更新しました。BankAccountCode={BankAccountCode}", bankAccount.BankAccountCode);
        return current;
    }

    public async Task DeactivateAsync(BankAccount bankAccount, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.BankAccounts
            .SingleOrDefaultAsync(b => b.BankAccountCode == bankAccount.BankAccountCode, cancellationToken)
            ?? throw new BankAccountValidationException($"銀行口座コード「{bankAccount.BankAccountCode}」は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(bankAccount.RowVersion!))
        {
            throw new BankAccountConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        var isLinkedToCustomer = await dbContext.Customers
            .AsNoTracking()
            .AnyAsync(c => !c.IsDeleted
                && (c.BankAccountCode1 == current.BankAccountCode || c.BankAccountCode2 == current.BankAccountCode),
                cancellationToken);
        if (isLinkedToCustomer)
        {
            throw new BankAccountValidationException(
                "この口座を振込先に指定している得意先があるため無効化できません。先に得意先マスタの指定を外してください。");
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
            throw new BankAccountConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("銀行口座を無効化しました。BankAccountCode={BankAccountCode}", bankAccount.BankAccountCode);
    }
}

/// <summary>銀行口座マスタの業務ルール違反（形式チェック済みの入力に対する業務的な拒否）。</summary>
public sealed class BankAccountValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class BankAccountConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
