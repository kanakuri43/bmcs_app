using bmcs_app.Application.Master;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BillingEntity = bmcs_app.Domain.Entities.Billing;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Master;

/// <summary>
/// 親子請求（請求集約）のリンク検証・変更可否判定（docs/database-schema.md 1-1節、
/// 2026-09-29確定）の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する。
/// </summary>
/// <remarks>
/// <see cref="CustomerService"/> は自前で<c>BeginTransactionAsync</c>しないため、
/// <c>SettlementServiceTests</c>と同じ「外側をトランザクションで包み、テストの最後に
/// 必ずRollbackする」方式が使える。得意先・売上・請求データをすべてこのトランザクション内で
/// 作成するため、seedデータには一切触れず、後始末の物理削除も不要。
/// </remarks>
public class CustomerServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    [Fact]
    public async Task 請求得意先コード未指定なら自分自身が設定される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string code = "__TSC01";
            var created = await service.CreateAsync(NewCustomer(code, TaxUnit.Invoice, 15, RoundingType.Floor, billingCustomerCode: ""));

            Assert.Equal(code, created.BillingCustomerCode);
            Assert.True(created.IsBillingRoot);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 請求集約先を設定して登録できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string rootCode = "__TSC02R";
            const string childCode = "__TSC02C";
            await service.CreateAsync(NewCustomer(rootCode, TaxUnit.Invoice, 15, RoundingType.Floor, billingCustomerCode: rootCode));

            var child = await service.CreateAsync(
                NewCustomer(childCode, TaxUnit.Invoice, 15, RoundingType.Floor, billingCustomerCode: rootCode));

            Assert.Equal(rootCode, child.BillingCustomerCode);
            Assert.False(child.IsBillingRoot);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 締め日が異なる得意先を請求集約先に指定すると業務例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string rootCode = "__TSC03R";
            const string childCode = "__TSC03C";
            await service.CreateAsync(NewCustomer(rootCode, TaxUnit.Invoice, 15, RoundingType.Floor, billingCustomerCode: rootCode));

            await Assert.ThrowsAsync<CustomerValidationException>(() => service.CreateAsync(
                NewCustomer(childCode, TaxUnit.Invoice, 20, RoundingType.Floor, billingCustomerCode: rootCode)));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 確定済み請求に取り込まれた売上がある得意先は請求得意先コードを変更できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string customerCode = "__TSC04";
            const string otherRootCode = "__TSC04R";
            await service.CreateAsync(NewCustomer(customerCode, TaxUnit.Invoice, 15, RoundingType.Floor, billingCustomerCode: customerCode));
            await service.CreateAsync(NewCustomer(otherRootCode, TaxUnit.Invoice, 15, RoundingType.Floor, billingCustomerCode: otherRootCode));

            await InsertBillingAsync(dbContext, "__TBIL_C04", customerCode, TaxUnit.Invoice, currentBillingAmount: 10000m);
            await InsertSalesAsync(dbContext, NewClosingSalesLine(customerCode, TaxUnit.Invoice, "__TSAL_C04", 1, 10000m, "__TBIL_C04"));

            var current = await service.GetByCodeAsync(customerCode) ?? throw new InvalidOperationException();
            current.BillingCustomerCode = otherRootCode;

            await Assert.ThrowsAsync<CustomerValidationException>(() => service.UpdateAsync(current));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 請求集約元が残っている得意先は請求集約先を変更できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string rootCode = "__TSC05R";
            const string childCode = "__TSC05C";
            const string otherRootCode = "__TSC05O";
            await service.CreateAsync(NewCustomer(rootCode, TaxUnit.Invoice, 15, RoundingType.Floor, billingCustomerCode: rootCode));
            await service.CreateAsync(NewCustomer(childCode, TaxUnit.Invoice, 15, RoundingType.Floor, billingCustomerCode: rootCode));
            await service.CreateAsync(NewCustomer(otherRootCode, TaxUnit.Invoice, 15, RoundingType.Floor, billingCustomerCode: otherRootCode));

            var root = await service.GetByCodeAsync(rootCode) ?? throw new InvalidOperationException();
            root.BillingCustomerCode = otherRootCode;

            await Assert.ThrowsAsync<CustomerValidationException>(() => service.UpdateAsync(root));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 請求集約元が残っている得意先は無効化できない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string rootCode = "__TSC06R";
            const string childCode = "__TSC06C";
            await service.CreateAsync(NewCustomer(rootCode, TaxUnit.Invoice, 15, RoundingType.Floor, billingCustomerCode: rootCode));
            await service.CreateAsync(NewCustomer(childCode, TaxUnit.Invoice, 15, RoundingType.Floor, billingCustomerCode: rootCode));

            var root = await service.GetByCodeAsync(rootCode) ?? throw new InvalidOperationException();

            await Assert.ThrowsAsync<CustomerValidationException>(() => service.DeactivateAsync(root));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 請求集約元をさらに請求集約先に指定すると孫の禁止によりDB制約で拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, _) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string rootCode = "__TSC07R";
            const string childCode = "__TSC07C";
            const string grandchildCode = "__TSC07G";
            await InsertCustomerRawAsync(dbContext, rootCode, 15, TaxUnit.Invoice, RoundingType.Floor, rootCode);
            await InsertCustomerRawAsync(dbContext, childCode, 15, TaxUnit.Invoice, RoundingType.Floor, rootCode);

            await Assert.ThrowsAsync<SqlException>(() =>
                InsertCustomerRawAsync(dbContext, grandchildCode, 15, TaxUnit.Invoice, RoundingType.Floor, childCode));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 締め日が異なる親子はDB制約で拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, _) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string rootCode = "__TSC08R";
            const string childCode = "__TSC08C";
            await InsertCustomerRawAsync(dbContext, rootCode, 15, TaxUnit.Invoice, RoundingType.Floor, rootCode);

            await Assert.ThrowsAsync<SqlException>(() =>
                InsertCustomerRawAsync(dbContext, childCode, 20, TaxUnit.Invoice, RoundingType.Floor, rootCode));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 都度得意先を請求集約元にするとDB制約で拒否される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, _) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            const string rootCode = "__TSC09R";
            const string childCode = "__TSC09C";
            await InsertCustomerRawAsync(dbContext, rootCode, 0, TaxUnit.Line, RoundingType.Floor, rootCode);

            await Assert.ThrowsAsync<SqlException>(() =>
                InsertCustomerRawAsync(dbContext, childCode, 0, TaxUnit.Line, RoundingType.Floor, rootCode));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static (BmcsDbContext DbContext, CustomerService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<CustomerService>());

    private static CustomerEntity NewCustomer(
        string customerCode, TaxUnit taxUnit, byte closingDay, RoundingType roundingType, string billingCustomerCode)
    {
        var now = DateTime.Now;
        return new CustomerEntity
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = closingDay,
            TaxUnit = taxUnit,
            RoundingType = roundingType,
            PrintRepresentativeFlag = false,
            BillingCustomerCode = billingCustomerCode,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// DB制約そのものを検証するため、EFのChangeTracker・アプリ層バリデーションを経由せず
    /// 生SQLで直接INSERTする。
    /// </summary>
    private static Task InsertCustomerRawAsync(
        BmcsDbContext dbContext, string customerCode, byte closingDay, TaxUnit taxUnit, RoundingType roundingType,
        string billingCustomerCode)
        => dbContext.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO dbo.customers
                (customer_code, customer_name, closing_day, tax_unit, rounding_type, print_representative_flag,
                 billing_customer_code, created_by, created_at, updated_by, updated_at)
            VALUES
                ({customerCode}, N'テスト用得意先', {closingDay}, {(byte)taxUnit}, {(byte)roundingType}, 0,
                 {billingCustomerCode}, N'TEST', SYSDATETIME(), N'TEST', SYSDATETIME())");

    private static Task InsertBillingAsync(
        BmcsDbContext dbContext, string billingNumber, string customerCode, TaxUnit taxUnit, decimal currentBillingAmount)
    {
        var now = DateTime.Now;
        dbContext.Billings.Add(new BillingEntity
        {
            BillingNumber = billingNumber,
            CustomerCode = customerCode,
            TaxUnit = taxUnit,
            CustomerName = "テスト用得意先",
            BillingDate = new DateOnly(2026, 7, 20),
            ClosingYearMonth = "202607",
            PreviousBalance = 0m,
            ReceiptAmount = 0m,
            SalesAmount = currentBillingAmount,
            TaxAmount = 0m,
            CurrentBillingAmount = currentBillingAmount,
            StandardRateTaxableAmount = 0m,
            StandardRateTaxAmount = 0m,
            ReducedRateTaxableAmount = 0m,
            ReducedRateTaxAmount = 0m,
            TaxExemptAmount = 0m,
            BillingStatus = BillingStatus.Confirmed,
            ConfirmedAt = now,
            ConfirmedBy = "TEST",
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static Task InsertSalesAsync(BmcsDbContext dbContext, params SalesEntity[] lines)
    {
        dbContext.Sales.AddRange(lines);
        return dbContext.SaveChangesAsync();
    }

    private static SalesEntity NewClosingSalesLine(
        string customerCode, TaxUnit taxUnit, string slipNumber, short lineNumber, decimal amount, string billingNumber)
    {
        var now = DateTime.Now;
        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = lineNumber,
            SlipDate = new DateOnly(2026, 7, 1),
            CustomerCode = customerCode,
            TaxUnit = taxUnit,
            CustomerName = "テスト用得意先",
            SlipType = SlipType.Sales,
            ProductCode = "1001",
            ProductName = "テスト用商品",
            Quantity = 1m,
            UnitPrice = amount,
            Amount = amount,
            CostPrice = 0m,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            SlipTaxAmount = null,
            TaxAmount = null,
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Billed,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            BillingNumber = billingNumber,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }
}
