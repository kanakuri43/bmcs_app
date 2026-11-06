using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Tests.Sales;

/// <summary>
/// 商品検索「過去の取引履歴から」軸の結合テスト。外側をトランザクションで包み、最後に必ず Rollback するので DB には何も残らない。
/// </summary>
public class ProductHistoryQueryServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const string Customer = "CUS001";
    private const string Product = "PRD001";

    [Fact]
    public async Task 同じ商品でも仕入先が違えば別の行になり_同じ仕入先は最新の行だけが残る()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.Now;
            db.Suppliers.AddRange(
                new Supplier { SupplierCode = "__HSUP1", SupplierName = "A", CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now },
                new Supplier { SupplierCode = "__HSUP2", SupplierName = "B", CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now });
            await db.SaveChangesAsync();

            db.Sales.AddRange(
                NewLine("__HIST-1", new(2020, 4, 1), 100m, "__HSUP1"),
                NewLine("__HIST-2", new(2020, 4, 2), 110m, "__HSUP1"),   // __HSUP1 の最新
                NewLine("__HIST-3", new(2020, 4, 3), 120m, "__HSUP2"),
                NewLine("__HIST-4", new(2020, 4, 4), 130m, null));        // 仕入先なし
            await db.SaveChangesAsync();

            var hits = (await scope.ServiceProvider.GetRequiredService<ProductHistoryQueryService>().SearchAsync(Customer))
                .Where(h => h.ProductCode == Product && h.LastSlipDate.Year == 2020)
                .ToList();

            // 伝票日付の新しい順（仕入先なし → __HSUP2 → __HSUP1）。
            Assert.Equal([null, "__HSUP2", "__HSUP1"], hits.Select(h => h.SupplierCode));
            Assert.Equal([130m, 120m, 110m], hits.Select(h => h.LastUnitPrice));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static SalesEntity NewLine(string slipNumber, DateOnly slipDate, decimal unitPrice, string? supplierCode)
    {
        var now = DateTime.Now;
        return new SalesEntity
        {
            SalesSlipNumber = slipNumber,
            LineNumber = 1,
            SlipDate = slipDate,
            CustomerCode = Customer,
            TaxUnit = TaxUnit.Invoice,
            CustomerName = "テスト用得意先",
            SlipType = SlipType.Sales,
            ProductCode = Product,
            ProductName = "テスト用商品",
            Quantity = 1m,
            UnitPrice = unitPrice,
            Amount = unitPrice,
            CostPrice = 50m,
            SupplierCode = supplierCode,
            TaxCategory = TaxCategory.Standard,
            TaxRate = 10m,
            DeliveryNoteIssueCount = 0,
            BillingStatus = BillingLinkStatus.Unbilled,
            SettlementStatus = SettlementStatus.Unsettled,
            SettledAmount = 0m,
            CreatedBy = "TEST", CreatedAt = now, UpdatedBy = "TEST", UpdatedAt = now,
        };
    }
}
