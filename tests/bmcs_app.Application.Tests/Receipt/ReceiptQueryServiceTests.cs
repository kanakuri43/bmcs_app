using bmcs_app.Application.Receipt;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using ReceiptEntity = bmcs_app.Domain.Entities.Receipt;

namespace bmcs_app.Application.Tests.Receipt;

/// <summary>
/// 伝票検索モーダル用の締め入金検索（<see cref="ReceiptQueryService.SearchAsync"/>）の結合テスト。
/// 開発用ライブDB（172.16.3.171）に対して実行し、テストの最後に必ずRollbackする。
/// </summary>
public class ReceiptQueryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerCode = "__TSTRCQ1";

    /// <summary>
    /// <see cref="Domain.Entities.Receipt.Amount"/> は行単位の値（伝票合計はSUMして求める）。
    /// 伝票単位への集約が正しく行われることを検証する。
    /// </summary>
    [Fact]
    public async Task 複数の支払手段内訳を持つ伝票の金額が合算される()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerCode);
            dbContext.Receipts.Add(NewReceipt(CustomerCode, "__TSTRCQ_SLIP1", 1, new DateOnly(2026, 8, 1), 6_000m));
            dbContext.Receipts.Add(NewReceipt(CustomerCode, "__TSTRCQ_SLIP1", 2, new DateOnly(2026, 8, 1), 4_000m));
            await dbContext.SaveChangesAsync();

            var results = await service.SearchAsync(CustomerCode);

            var hit = Assert.Single(results, h => h.ReceiptSlipNumber == "__TSTRCQ_SLIP1");
            Assert.Equal(10_000m, hit.ReceiptAmount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task 入金No得意先コード得意先名のいずれでも検索できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerCode);
            dbContext.Receipts.Add(NewReceipt(CustomerCode, "__TSTRCQ_KW1", 1, new DateOnly(2026, 8, 1), 1_000m));
            await dbContext.SaveChangesAsync();

            var byNumber = await service.SearchAsync("__TSTRCQ_KW1");
            Assert.Single(byNumber, h => h.ReceiptSlipNumber == "__TSTRCQ_KW1");

            var byCustomerCode = await service.SearchAsync(CustomerCode);
            Assert.Contains(byCustomerCode, h => h.ReceiptSlipNumber == "__TSTRCQ_KW1");

            var byCustomerName = await service.SearchAsync("テスト用得意先");
            Assert.Contains(byCustomerName, h => h.ReceiptSlipNumber == "__TSTRCQ_KW1");

            var byUnrelatedKeyword = await service.SearchAsync("__NOMATCH_KEYWORD__");
            Assert.DoesNotContain(byUnrelatedKeyword, h => h.ReceiptSlipNumber == "__TSTRCQ_KW1");
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static (BmcsDbContext DbContext, ReceiptQueryService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<ReceiptQueryService>());

    private static Task InsertCustomerAsync(BmcsDbContext dbContext, string customerCode)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new CustomerEntity
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = 20,
            TaxUnit = TaxUnit.Invoice,
            RoundingType = RoundingType.Floor,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static ReceiptEntity NewReceipt(
        string customerCode, string receiptSlipNumber, short lineNumber, DateOnly receiptDate, decimal amount)
    {
        var now = DateTime.Now;
        return new ReceiptEntity
        {
            ReceiptSlipNumber = receiptSlipNumber,
            LineNumber = lineNumber,
            ReceiptDate = receiptDate,
            CustomerCode = customerCode,
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "テスト用得意先",
            DepositMethodCode = "TRANSFER",
            BankAccountCode = "1",
            Amount = amount,
            AllocationStatus = AllocationStatus.Unallocated,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }
}
