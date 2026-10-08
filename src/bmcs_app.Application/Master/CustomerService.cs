using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
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
/// 親子請求（請求集約）のリンク検証（docs/database-schema.md 1-1節）も担う。
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

        if (string.IsNullOrWhiteSpace(customer.BillingCustomerCode))
        {
            // 未指定なら自分自身（＝単独で請求、従来どおり）を設定する。
            customer.BillingCustomerCode = customer.CustomerCode;
        }

        var exists = await dbContext.Customers
            .AsNoTracking()
            .AnyAsync(c => c.CustomerCode == customer.CustomerCode, cancellationToken);
        if (exists)
        {
            throw new CustomerValidationException($"得意先コード「{customer.CustomerCode}」は既に登録されています。");
        }

        await ValidateBillingAggregationLinkAsync(customer, cancellationToken);
        await ValidateBankAccountLinksAsync(customer, cancellationToken);

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
            // 端数区分が変更可能だと、発行済み伝票の消費税額を
            // 後から再現できなくなり、請求締めで金額が合わなくなる。
            // 締め区分・税区分と同じく登録後は不変にする。
            throw new CustomerValidationException("締め区分・税区分・端数区分は登録後変更できません。");
        }

        if (current.BillingCustomerCode != customer.BillingCustomerCode)
        {
            if (await HasBillingChangeLockAsync(current, cancellationToken))
            {
                throw new CustomerValidationException(
                    "確定済みの請求に取り込まれた売上がある、または請求集約元の得意先が存在するため、請求得意先コードを変更できません。");
            }

            await ValidateBillingAggregationLinkAsync(customer, cancellationToken);
            current.BillingCustomerCode = customer.BillingCustomerCode;
        }

        if (current.BankAccountCode1 != customer.BankAccountCode1 || current.BankAccountCode2 != customer.BankAccountCode2)
        {
            // 変更した場合のみ検証する（BillingCustomerCode と同じ方針）。既存の紐づけを
            // そのまま保存し直すだけなら、紐づけ先の口座が後から無効化されていても拒否しない
            // （帳票側は無効化済みの口座を印字対象から除外するだけで、保存自体は妨げない）。
            await ValidateBankAccountLinksAsync(customer, cancellationToken);
            current.BankAccountCode1 = customer.BankAccountCode1;
            current.BankAccountCode2 = customer.BankAccountCode2;
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

        if (current.IsBillingRoot && await HasActiveBillingChildrenAsync(current.CustomerCode, cancellationToken))
        {
            throw new CustomerValidationException(
                "請求集約元の得意先が存在するため無効化できません。先に請求集約元の請求得意先コードを変更してください。");
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

    /// <summary>
    /// 請求得意先コードを変更できるかどうか（業務ルール7、docs/database-schema.md 1-1節）。
    /// 得意先マスタ画面が入力欄の編集可否を決めるために呼ぶ。
    /// </summary>
    public async Task<bool> CanChangeBillingCustomerAsync(string customerCode, CancellationToken cancellationToken = default)
    {
        var current = await dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.CustomerCode == customerCode, cancellationToken)
            ?? throw new CustomerValidationException($"得意先コード「{customerCode}」は見つかりません。");

        return !await HasBillingChangeLockAsync(current, cancellationToken);
    }

    /// <summary>
    /// 請求得意先コードの変更を禁止すべきか（業務ルール7）。次のいずれかに該当すれば変更不可。
    /// 1. 確定済み請求（billings）に取り込まれた売上（sales.billing_number IS NOT NULL）が1件でもある
    /// 2. 自分が請求集約先で、確定済み billings を持つ（請求集約先→請求集約元への降格を禁止）
    /// 3. 自分が請求集約先で、有効な請求集約元が残っている（先に請求集約元を外させる）
    /// </summary>
    private async Task<bool> HasBillingChangeLockAsync(Customer current, CancellationToken cancellationToken)
    {
        var hasBilledSales = await dbContext.Sales
            .AsNoTracking()
            .AnyAsync(s => s.CustomerCode == current.CustomerCode && !s.IsDeleted && s.BillingNumber != null, cancellationToken);
        if (hasBilledSales)
        {
            return true;
        }

        if (!current.IsBillingRoot)
        {
            return false;
        }

        var hasConfirmedBillings = await dbContext.Billings
            .AsNoTracking()
            .AnyAsync(b => b.CustomerCode == current.CustomerCode && !b.IsDeleted && b.BillingStatus == BillingStatus.Confirmed, cancellationToken);
        if (hasConfirmedBillings)
        {
            return true;
        }

        return await HasActiveBillingChildrenAsync(current.CustomerCode, cancellationToken);
    }

    private Task<bool> HasActiveBillingChildrenAsync(string customerCode, CancellationToken cancellationToken)
        => dbContext.Customers
            .AsNoTracking()
            .AnyAsync(c => c.BillingCustomerCode == customerCode && c.CustomerCode != customerCode && !c.IsDeleted, cancellationToken);

    /// <summary>
    /// <see cref="BillingAggregationValidator"/> による請求集約リンクの検証。
    /// 自分自身を指す場合は指し先の照会をスキップする。
    /// </summary>
    private async Task ValidateBillingAggregationLinkAsync(Customer candidate, CancellationToken cancellationToken)
    {
        Customer? billingCustomer = candidate.BillingCustomerCode == candidate.CustomerCode
            ? null
            : await dbContext.Customers
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.CustomerCode == candidate.BillingCustomerCode, cancellationToken);

        var reason = BillingAggregationValidator.Validate(candidate, billingCustomer);
        if (reason is not null)
        {
            throw new CustomerValidationException(reason);
        }
    }

    /// <summary>
    /// 請求書へ印字する振込先口座（最大2件）の紐づけを検証する。
    /// 同一口座の二重紐づけ・存在しない口座・論理削除済みの口座を拒否する
    /// （DBのFK/CHECK制約の生の例外が画面にそのまま出ないようにする）。
    /// </summary>
    private async Task ValidateBankAccountLinksAsync(Customer candidate, CancellationToken cancellationToken)
    {
        var codes = new[] { candidate.BankAccountCode1, candidate.BankAccountCode2 }
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .ToList();
        if (codes.Count == 0)
        {
            return;
        }

        if (codes.Count != codes.Distinct().Count())
        {
            throw new CustomerValidationException("振込先口座1と口座2に同じ口座を指定することはできません。");
        }

        var validCodes = await dbContext.BankAccounts
            .AsNoTracking()
            .Where(b => codes.Contains(b.BankAccountCode) && !b.IsDeleted)
            .Select(b => b.BankAccountCode)
            .ToListAsync(cancellationToken);

        var missing = codes.Except(validCodes).ToList();
        if (missing.Count > 0)
        {
            throw new CustomerValidationException(
                $"振込先口座が見つかりません。BankAccountCode={string.Join(", ", missing)}");
        }
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
