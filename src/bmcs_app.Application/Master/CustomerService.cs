using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Master;

/// <summary>
/// 得意先マスタのユースケース。
/// 税区分×締日の整合（docs/database-schema.md の CK_customers_tax_unit_closing_day）と、
/// 登録後の締め区分・税区分の変更禁止をここで担保する（DB の CHECK 制約は最終防衛線）。
/// </summary>
public class CustomerService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<CustomerService> logger)
{
    public Task<List<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default)
        => dbContext.Customers
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.CustomerCode)
            .ToListAsync(cancellationToken);

    public Task<Customer?> GetByCodeAsync(string customerCode, CancellationToken cancellationToken = default)
        => dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.CustomerCode == customerCode, cancellationToken);

    public async Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ValidateTaxUnitClosingDayCombo(customer.ClosingDay, customer.TaxUnit);

        var exists = await dbContext.Customers
            .AsNoTracking()
            .AnyAsync(c => c.CustomerCode == customer.CustomerCode, cancellationToken);
        if (exists)
        {
            throw new CustomerValidationException($"得意先コード「{customer.CustomerCode}」は既に登録されています。");
        }

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;
        customer.CreatedBy = employeeCode;
        customer.CreatedAt = now;
        customer.UpdatedBy = employeeCode;
        customer.UpdatedAt = now;
        customer.IsDeleted = false;

        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("得意先を登録しました。CustomerCode={CustomerCode}", customer.CustomerCode);
        return customer;
    }

    public async Task<Customer> UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ValidateTaxUnitClosingDayCombo(customer.ClosingDay, customer.TaxUnit);

        var current = await dbContext.Customers
            .SingleOrDefaultAsync(c => c.CustomerCode == customer.CustomerCode, cancellationToken)
            ?? throw new CustomerValidationException($"得意先コード「{customer.CustomerCode}」は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(customer.RowVersion!))
        {
            throw new CustomerConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }

        if (current.ClosingDay != customer.ClosingDay
            || current.TaxUnit != customer.TaxUnit
            || current.RoundingType != customer.RoundingType)
        {
            // 端数区分が変更可能だと、発行済み伝票の消費税額（TODO.md 5-1）を
            // 後から再現できなくなり、請求締め（Phase 6）で金額が合わなくなる。
            // 締め区分・税区分と同じく登録後は不変にする（2026-09-08 ユーザー確認済み）。
            throw new CustomerValidationException("締め区分・税区分・端数区分は登録後変更できません。");
        }

        current.CustomerName = customer.CustomerName;
        current.CustomerNameKana = customer.CustomerNameKana;
        current.PostalCode = customer.PostalCode;
        current.Address1 = customer.Address1;
        current.Address2 = customer.Address2;
        current.PhoneNumber = customer.PhoneNumber;
        current.FaxNumber = customer.FaxNumber;
        current.ContactPersonName = customer.ContactPersonName;
        current.SalesEmployeeCode = customer.SalesEmployeeCode;
        current.PrintRepresentativeFlag = customer.PrintRepresentativeFlag;
        current.UpdatedBy = currentEmployeeContext.EmployeeCode;
        current.UpdatedAt = DateTime.Now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new CustomerConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("得意先を更新しました。CustomerCode={CustomerCode}", customer.CustomerCode);
        return current;
    }

    public async Task DeactivateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.Customers
            .SingleOrDefaultAsync(c => c.CustomerCode == customer.CustomerCode, cancellationToken)
            ?? throw new CustomerValidationException($"得意先コード「{customer.CustomerCode}」は見つかりません。");

        if (!current.RowVersion!.SequenceEqual(customer.RowVersion!))
        {
            throw new CustomerConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
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
            throw new CustomerConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("得意先を無効化しました。CustomerCode={CustomerCode}", customer.CustomerCode);
    }

    private static void ValidateTaxUnitClosingDayCombo(byte closingDay, TaxUnit taxUnit)
    {
        var valid = (taxUnit == TaxUnit.Line && closingDay == 0)
            || (taxUnit is TaxUnit.Invoice or TaxUnit.Slip && (closingDay is >= 1 and <= 31 or 99));

        if (!valid)
        {
            throw new CustomerValidationException(
                "税区分と締め日の組み合わせが不正です。都度取引は内税明細単位・締日なし、締め取引は請求単位/伝票単位・締日1〜31・99の組み合わせにしてください。");
        }
    }
}

/// <summary>得意先マスタの業務ルール違反（形式チェック済みの入力に対する業務的な拒否）。</summary>
public sealed class CustomerValidationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class CustomerConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
