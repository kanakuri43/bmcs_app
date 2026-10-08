using bmcs_app.Application.Master;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using BankAccountEntity = bmcs_app.Domain.Entities.BankAccount;
using CompanyInfoEntity = bmcs_app.Domain.Entities.CompanyInfo;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using DepositMethodEntity = bmcs_app.Domain.Entities.DepositMethod;
using EmployeeEntity = bmcs_app.Domain.Entities.Employee;
using ProductEntity = bmcs_app.Domain.Entities.Product;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// マスタ更新の楽観的排他制御（docs/architecture.md 9章）の結合テスト。
/// 「別の端末が先に更新した」状態を、読み込み時点の RowVersion（古い値）を持つ別オブジェクトで
/// 再現する。外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// </summary>
/// <remarks>
/// 検証する性質: 古い RowVersion での更新・無効化は各マスタの <c>*ConcurrencyException</c> になり、
/// 先に保存された値が後勝ちで上書きされない。
/// </remarks>
public class MasterConcurrencyTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    [Fact]
    public Task 商品マスタ_古いRowVersionでの更新と無効化は競合になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<ProductService>();
        var created = await service.CreateAsync(NewProduct("__TCP01", "初期", null));
        var stale = (byte[])created.RowVersion!.Clone();

        await service.UpdateAsync(NewProduct("__TCP01", "先勝ち", stale));

        await Assert.ThrowsAsync<ProductConcurrencyException>(() => service.UpdateAsync(NewProduct("__TCP01", "後負け", stale)));
        await Assert.ThrowsAsync<ProductConcurrencyException>(() => service.DeactivateAsync(NewProduct("__TCP01", "後負け", stale)));
        Assert.Equal("先勝ち", (await service.GetByCodeAsync("__TCP01"))!.ProductName);
    });

    [Fact]
    public Task 社員マスタ_古いRowVersionでの更新と無効化は競合になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<EmployeeService>();
        var created = await service.CreateAsync(NewEmployee("__TCE01", "初期", null));
        var stale = (byte[])created.RowVersion!.Clone();

        await service.UpdateAsync(NewEmployee("__TCE01", "先勝ち", stale));

        await Assert.ThrowsAsync<EmployeeConcurrencyException>(() => service.UpdateAsync(NewEmployee("__TCE01", "後負け", stale)));
        await Assert.ThrowsAsync<EmployeeConcurrencyException>(() => service.DeactivateAsync(NewEmployee("__TCE01", "後負け", stale)));
        Assert.Equal("先勝ち", (await service.GetByCodeAsync("__TCE01"))!.EmployeeName);
    });

    [Fact]
    public Task 銀行口座マスタ_古いRowVersionでの更新と無効化は競合になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<BankAccountService>();
        var created = await service.CreateAsync(NewBankAccount("__TCB01", "初期", null));
        var stale = (byte[])created.RowVersion!.Clone();

        await service.UpdateAsync(NewBankAccount("__TCB01", "先勝ち", stale));

        await Assert.ThrowsAsync<BankAccountConcurrencyException>(() => service.UpdateAsync(NewBankAccount("__TCB01", "後負け", stale)));
        await Assert.ThrowsAsync<BankAccountConcurrencyException>(() => service.DeactivateAsync(NewBankAccount("__TCB01", "後負け", stale)));
        Assert.Equal("先勝ち", (await service.GetByCodeAsync("__TCB01"))!.BankName);
    });

    [Fact]
    public Task 入金方法マスタ_古いRowVersionでの更新と無効化は競合になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<DepositMethodService>();
        var created = await service.CreateAsync(NewDepositMethod("__TCD01", "初期", null));
        var stale = (byte[])created.RowVersion!.Clone();

        await service.UpdateAsync(NewDepositMethod("__TCD01", "先勝ち", stale));

        await Assert.ThrowsAsync<DepositMethodConcurrencyException>(() => service.UpdateAsync(NewDepositMethod("__TCD01", "後負け", stale)));
        await Assert.ThrowsAsync<DepositMethodConcurrencyException>(() => service.DeactivateAsync(NewDepositMethod("__TCD01", "後負け", stale)));
        Assert.Equal("先勝ち", (await service.GetByCodeAsync("__TCD01"))!.DepositMethodName);
    });

    [Fact]
    public Task 得意先マスタ_古いRowVersionでの更新と無効化は競合になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CustomerService>();
        var created = await service.CreateAsync(NewCustomer("__TCC01", "初期", null));
        var stale = (byte[])created.RowVersion!.Clone();

        await service.UpdateAsync(NewCustomer("__TCC01", "先勝ち", stale));

        await Assert.ThrowsAsync<CustomerConcurrencyException>(() => service.UpdateAsync(NewCustomer("__TCC01", "後負け", stale)));
        await Assert.ThrowsAsync<CustomerConcurrencyException>(() => service.DeactivateAsync(NewCustomer("__TCC01", "後負け", stale)));
        Assert.Equal("先勝ち", (await service.GetByCodeAsync("__TCC01"))!.CustomerName);
    });

    [Fact]
    public Task 自社情報_古いRowVersionでの保存は競合になる() => RunAsync(async sp =>
    {
        var service = sp.GetRequiredService<CompanyInfoService>();
        var existing = await service.GetAsync();
        if (existing is null)
        {
            return; // 自社情報が未登録の環境では「更新」自体が存在しないため検証対象外。
        }

        var stale = (byte[])existing.RowVersion!.Clone();
        await service.SaveAsync(NewCompanyInfo("先勝ち", stale));

        await Assert.ThrowsAsync<CompanyInfoConcurrencyException>(() => service.SaveAsync(NewCompanyInfo("後負け", stale)));
        Assert.Equal("先勝ち", (await service.GetAsync())!.CompanyName);
    });

    private async Task RunAsync(Func<IServiceProvider, Task> body)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await body(scope.ServiceProvider);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static ProductEntity NewProduct(string code, string name, byte[]? rowVersion) => new()
    {
        ProductCode = code,
        ProductName = name,
        StandardUnitPriceExclTax = 100,
        StandardUnitPriceInclTax = 110,
        StandardCostPrice = 50,
        TaxCategory = TaxCategory.Standard,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        RowVersion = rowVersion,
    };

    private static EmployeeEntity NewEmployee(string code, string name, byte[]? rowVersion) => new()
    {
        EmployeeCode = code,
        EmployeeName = name,
        PermissionLevel = 1,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        RowVersion = rowVersion,
    };

    private static BankAccountEntity NewBankAccount(string code, string bankName, byte[]? rowVersion) => new()
    {
        BankAccountCode = code,
        BankName = bankName,
        BranchName = "テスト支店",
        AccountType = BankAccountType.Ordinary,
        AccountNumber = "1234567",
        AccountHolderName = "テスト",
        DisplayOrder = 1,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        RowVersion = rowVersion,
    };

    private static DepositMethodEntity NewDepositMethod(string code, string name, byte[]? rowVersion) => new()
    {
        DepositMethodCode = code,
        DepositMethodName = name,
        RequiresBankAccount = false,
        RequiresBillDueDate = false,
        DisplayOrder = 1,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        RowVersion = rowVersion,
    };

    private static CustomerEntity NewCustomer(string code, string name, byte[]? rowVersion) => new()
    {
        CustomerCode = code,
        CustomerName = name,
        ClosingDay = 15,
        TaxUnit = TaxUnit.Invoice,
        RoundingType = RoundingType.Floor,
        PrintRepresentativeFlag = false,
        BillingCustomerCode = code,
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        RowVersion = rowVersion,
    };

    private static CompanyInfoEntity NewCompanyInfo(string name, byte[] rowVersion) => new()
    {
        CompanyInfoId = 1,
        CompanyName = name,
        InvoiceRegistrationNumber = "T0000000000000",
        CreatedBy = "TEST", CreatedAt = DateTime.Now, UpdatedBy = "TEST", UpdatedAt = DateTime.Now,
        RowVersion = rowVersion,
    };
}
