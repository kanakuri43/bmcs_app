using bmcs_app.Application.Receipt;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CustomerEntity = bmcs_app.Domain.Entities.Customer;
using DetailReceiptEntity = bmcs_app.Domain.Entities.DetailReceipt;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Receipt;

/// <summary>
/// 伝票検索モーダル用の明細入金検索（<see cref="DetailReceiptQueryService.SearchAsync"/>）の
/// 結合テスト。開発用ライブDB（172.16.3.171）に対して実行し、テストの最後に必ずRollbackする。
/// </summary>
public class DetailReceiptQueryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string CustomerCode = "__TSTDRQ1";

    [Fact]
    public async Task 明細入金No得意先コード得意先名のいずれでも検索できる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerCode);
            await InsertSalesAsync(dbContext, "__TSTDRQ_SAL1", 1_000m);
            dbContext.DetailReceipts.Add(
                NewDetailReceipt(CustomerCode, "__TSTDRQ_KW1", 1, new DateOnly(2026, 8, 1), 1_000m, "__TSTDRQ_SAL1"));
            await dbContext.SaveChangesAsync();

            var byNumber = await service.SearchAsync("__TSTDRQ_KW1");
            Assert.Single(byNumber, h => h.DetailReceiptNumber == "__TSTDRQ_KW1");

            var byCustomerCode = await service.SearchAsync(CustomerCode);
            Assert.Contains(byCustomerCode, h => h.DetailReceiptNumber == "__TSTDRQ_KW1");

            var byCustomerName = await service.SearchAsync("テスト用得意先");
            Assert.Contains(byCustomerName, h => h.DetailReceiptNumber == "__TSTDRQ_KW1");

            var byUnrelatedKeyword = await service.SearchAsync("__NOMATCH_KEYWORD__");
            Assert.DoesNotContain(byUnrelatedKeyword, h => h.DetailReceiptNumber == "__TSTDRQ_KW1");
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    /// <summary>
    /// <see cref="DetailReceiptEntity.ReceiptAmount"/> は伝票単位の値で、登録時に全行へ同値が
    /// 複写される（<c>DetailReceiptEntryService</c>）。行ごとにSUMすると二重計上になるため、
    /// 先頭行の値をそのまま使うのが正しい挙動であることを検証する。
    /// </summary>
    [Fact]
    public async Task 複数明細行を持つ伝票でも金額はSUMされず伝票単位の値のまま返る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var (dbContext, service) = Resolve(scope);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            await InsertCustomerAsync(dbContext, CustomerCode);
            await InsertSalesAsync(dbContext, "__TSTDRQ_SAL2", 1, 10_000m);
            await InsertSalesAsync(dbContext, "__TSTDRQ_SAL2", 2, 10_000m);

            var line1 = NewDetailReceipt(
                CustomerCode, "__TSTDRQ_MULTI1", 1, new DateOnly(2026, 8, 1), 10_000m, "__TSTDRQ_SAL2", targetLine: 1);
            line1.AllocatedAmount = 6_000m;
            var line2 = NewDetailReceipt(
                CustomerCode, "__TSTDRQ_MULTI1", 2, new DateOnly(2026, 8, 1), 10_000m, "__TSTDRQ_SAL2", targetLine: 2);
            line2.AllocatedAmount = 4_000m;
            dbContext.DetailReceipts.AddRange(line1, line2);
            await dbContext.SaveChangesAsync();

            var results = await service.SearchAsync("__TSTDRQ_MULTI1");

            var hit = Assert.Single(results, h => h.DetailReceiptNumber == "__TSTDRQ_MULTI1");
            Assert.Equal(10_000m, hit.ReceiptAmount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static (BmcsDbContext DbContext, DetailReceiptQueryService Service) Resolve(AsyncServiceScope scope) => (
        scope.ServiceProvider.GetRequiredService<BmcsDbContext>(),
        scope.ServiceProvider.GetRequiredService<DetailReceiptQueryService>());

    private static Task InsertCustomerAsync(BmcsDbContext dbContext, string customerCode)
    {
        var now = DateTime.Now;
        dbContext.Customers.Add(new CustomerEntity
        {
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            ClosingDay = 0,
            TaxUnit = TaxUnit.Line,
            RoundingType = RoundingType.Ceiling,
            PrintRepresentativeFlag = false,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static Task InsertSalesAsync(
        BmcsDbContext dbContext, string salesSlipNumber, decimal amount)
        => InsertSalesAsync(dbContext, salesSlipNumber, 1, amount);

    private static Task InsertSalesAsync(
        BmcsDbContext dbContext, string salesSlipNumber, short lineNumber, decimal amount)
    {
        var now = DateTime.Now;
        dbContext.Sales.Add(new SalesEntity
        {
            SalesSlipNumber = salesSlipNumber,
            LineNumber = lineNumber,
            SlipDate = new DateOnly(2026, 7, 20),
            CustomerCode = CustomerCode,
            TaxUnit = TaxUnit.Line,
            CustomerName = "テスト用得意先",
            SlipType = SlipType.Sales,
            ProductCode = "3001",
            ProductName = "テスト用商品",
            Quantity = 1m,
            UnitPrice = amount,
            Amount = amount,
            CostPrice = 0m,
            TaxCategory = TaxCategory.Reduced,
            TaxRate = 8m,
            SlipTaxAmount = null,
            TaxAmount = 0m,
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Unbilled,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            BillingNumber = null,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        });
        return dbContext.SaveChangesAsync();
    }

    private static DetailReceiptEntity NewDetailReceipt(
        string customerCode, string detailReceiptNumber, short lineNumber, DateOnly receiptDate,
        decimal receiptAmount, string targetSalesSlipNumber, short targetLine = 1)
    {
        var now = DateTime.Now;
        return new DetailReceiptEntity
        {
            DetailReceiptNumber = detailReceiptNumber,
            LineNumber = lineNumber,
            ReceiptDate = receiptDate,
            CustomerCode = customerCode,
            CustomerName = "テスト用得意先",
            DepositMethodCode = "TRANSFER",
            BankAccountCode = "1",
            ReceiptAmount = receiptAmount,
            TargetType = DetailReceiptTargetType.SalesLine,
            TargetSalesSlipNumber = targetSalesSlipNumber,
            TargetSalesLineNumber = targetLine,
            TargetDetailInvoiceNumber = null,
            AllocatedAmount = receiptAmount,
            FeeAdjustmentAmount = 0m,
            AllocationStatus = AllocationStatus.Unallocated,
            CreatedBy = "TEST",
            CreatedAt = now,
            UpdatedBy = "TEST",
            UpdatedAt = now,
        };
    }
}
