using bmcs_app.Application;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Tests;

/// <summary>
/// 開発用ライブDB（172.16.3.171）に対する結合テストの共通フィクスチャ。
/// <see cref="ApplicationServiceCollectionExtensions.AddApplication"/> を通してサービスを
/// 解決するため、DI 配線も含めて検証できる。
/// </summary>
/// <remarks>
/// テスト専用の使い捨てキー（<see cref="TestSequenceKey"/>）を <see cref="InitializeAsync"/>
/// で冪等に作成し、<see cref="DisposeAsync"/> で削除する。業務キー（<c>order_slip</c> 等）の
/// <c>current_value</c> は一切変更しない。前回実行がクラッシュして後始末できていなくても、
/// DELETE→INSERT の順で作り直すため再実行できる。
/// </remarks>
public sealed class DevDatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// テスト用の使い捨て採番キー。<see cref="bmcs_app.Domain.Enums.SlipNumberKind"/> の
    /// どの実キーとも衝突しないよう "__" プレフィックスにする。
    /// </summary>
    public const string TestSequenceKey = "__test_slip_number";

    /// <summary>テストが固定コードで参照するマスタ（社員・商品・銀行口座）。</summary>
    private static readonly string[] ReferenceEmployeeCodes = ["EMP001"];
    private static readonly string[] ReferenceProductCodes = ["PRD001", "PRD002"];
    private static readonly string[] ReferenceBankAccountCodes = ["BNK001", "1"];

    private ServiceProvider? _serviceProvider;
    private readonly List<string> _createdEmployees = [];
    private readonly List<string> _createdProducts = [];
    private readonly List<string> _createdBankAccounts = [];
    private readonly List<string> _createdCustomers = [];

    /// <summary>テストが参照する得意先（締め単位／伝票単位／内税明細単位を1つずつ）。seed_dev_data.sql の設定に合わせる。</summary>
    private static readonly (string Code, byte ClosingDay, TaxUnit TaxUnit, RoundingType Rounding)[] ReferenceCustomers =
    [
        ("CUS001", 20, TaxUnit.Invoice, RoundingType.Floor),
        ("CUS002", 99, TaxUnit.Slip, RoundingType.RoundHalfUp),
        ("CUS003", 0, TaxUnit.Line, RoundingType.Ceiling),
    ];

    public IServiceProvider Services => _serviceProvider
        ?? throw new InvalidOperationException("InitializeAsync が呼ばれていません。");

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(
                "appsettings.Development.json",
                optional: false)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(); // 各種サービスの ILogger<T> 依存を解決するため（既定はコンソール出力なし）
        services.AddApplication(configuration, []);
        _serviceProvider = services.BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();

        await dbContext.Database.ExecuteSqlAsync(
            $"DELETE FROM dbo.slip_number_sequences WHERE sequence_key = {TestSequenceKey}");
        await dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO dbo.slip_number_sequences
                (sequence_key, current_value, created_by, created_at, updated_by, updated_at)
            VALUES ({TestSequenceKey}, 0, N'TEST', SYSDATETIME(), N'TEST', SYSDATETIME())
            """);

        await EnsureReferenceMastersAsync(dbContext);
    }

    /// <summary>
    /// テストが固定コードで参照するマスタが開発用DBに無ければ作成する（既にあれば何もしない）。
    /// 開発用DBの内容に依存せずFK制約を満たすため。作成した行だけを <see cref="DisposeAsync"/> で削除する。
    /// </summary>
    private async Task EnsureReferenceMastersAsync(BmcsDbContext dbContext)
    {
        var now = DateTime.Now;

        foreach (var code in ReferenceEmployeeCodes.Where(c => !dbContext.Employees.Any(e => e.EmployeeCode == c)))
        {
            dbContext.Employees.Add(new Employee
            {
                EmployeeCode = code, EmployeeName = "テスト用社員", PermissionLevel = 1,
                CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
            });
            _createdEmployees.Add(code);
        }

        foreach (var code in ReferenceProductCodes.Where(c => !dbContext.Products.Any(p => p.ProductCode == c)))
        {
            dbContext.Products.Add(new Product
            {
                ProductCode = code, ProductName = "テスト用商品",
                StandardUnitPriceExclTax = 100, StandardUnitPriceInclTax = 110, StandardCostPrice = 50,
                TaxCategory = TaxCategory.Standard,
                CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
            });
            _createdProducts.Add(code);
        }

        foreach (var code in ReferenceBankAccountCodes.Where(c => !dbContext.BankAccounts.Any(a => a.BankAccountCode == c)))
        {
            dbContext.BankAccounts.Add(new BankAccount
            {
                BankAccountCode = code, BankName = "テスト銀行", BranchName = "テスト支店",
                AccountType = BankAccountType.Ordinary, AccountNumber = "0000000", AccountHolderName = "テスト",
                DisplayOrder = 1,
                CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
            });
            _createdBankAccounts.Add(code);
        }

        await dbContext.SaveChangesAsync();

        foreach (var (code, closingDay, taxUnit, rounding) in ReferenceCustomers
            .Where(c => !dbContext.Customers.Any(x => x.CustomerCode == c.Code)))
        {
            dbContext.Customers.Add(new Customer
            {
                CustomerCode = code, CustomerName = "テスト用参照得意先", ClosingDay = closingDay,
                TaxUnit = taxUnit, RoundingType = rounding, PrintRepresentativeFlag = false,
                BillingCustomerCode = code,
                CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
            });
            _createdCustomers.Add(code);
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        await dbContext.Database.ExecuteSqlAsync(
            $"DELETE FROM dbo.slip_number_sequences WHERE sequence_key = {TestSequenceKey}");

        // 作成した参照マスタだけを消す。他の行から参照されている（テストの後始末漏れ等）場合は残す。
        foreach (var code in _createdCustomers)
        {
            await TryDeleteAsync(dbContext, $"DELETE FROM dbo.customers WHERE customer_code = {code}");
        }

        foreach (var code in _createdProducts)
        {
            await TryDeleteAsync(dbContext, $"DELETE FROM dbo.products WHERE product_code = {code}");
        }

        foreach (var code in _createdBankAccounts)
        {
            await TryDeleteAsync(dbContext, $"DELETE FROM dbo.bank_accounts WHERE bank_account_code = {code}");
        }

        foreach (var code in _createdEmployees)
        {
            await TryDeleteAsync(dbContext, $"DELETE FROM dbo.employees WHERE employee_code = {code}");
        }

        await _serviceProvider.DisposeAsync();
    }

    private static async Task TryDeleteAsync(BmcsDbContext dbContext, FormattableString sql)
    {
        try
        {
            await dbContext.Database.ExecuteSqlAsync(sql);
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
        }
    }
}
