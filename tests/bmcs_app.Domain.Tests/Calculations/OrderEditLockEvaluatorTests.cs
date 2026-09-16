using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>受注の編集ロック（未売上のみ直接修正可。TODO.md 4-6・2026-09-16確定）の判定テスト。</summary>
public class OrderEditLockEvaluatorTests
{
    [Fact]
    public void 全行未売上なら編集可能()
    {
        var lines = new[] { NewLine(OrderStatus.NotSold) };

        var result = OrderEditLockEvaluator.Evaluate(lines);

        Assert.False(result.IsLocked);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void 一部売上を含むと編集不可()
    {
        var lines = new[] { NewLine(OrderStatus.PartiallySold) };

        var result = OrderEditLockEvaluator.Evaluate(lines);

        Assert.True(result.IsLocked);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void 売上完了を含むと編集不可()
    {
        var lines = new[] { NewLine(OrderStatus.FullySold) };

        var result = OrderEditLockEvaluator.Evaluate(lines);

        Assert.True(result.IsLocked);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void 中止済みを含むと編集不可()
    {
        var lines = new[] { NewLine(OrderStatus.Cancelled) };

        var result = OrderEditLockEvaluator.Evaluate(lines);

        Assert.True(result.IsLocked);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void 明細行のいずれか1行でも一部売上なら伝票全体が編集不可()
    {
        var lines = new[] { NewLine(OrderStatus.NotSold, lineNumber: 1), NewLine(OrderStatus.PartiallySold, lineNumber: 2) };

        var result = OrderEditLockEvaluator.Evaluate(lines);

        Assert.True(result.IsLocked);
    }

    [Fact]
    public void 状態混在時は中止済みが最優先で報告される()
    {
        var lines = new[]
        {
            NewLine(OrderStatus.Cancelled, lineNumber: 1),
            NewLine(OrderStatus.FullySold, lineNumber: 2),
        };

        var result = OrderEditLockEvaluator.Evaluate(lines);

        Assert.True(result.IsLocked);
        Assert.Contains("中止", result.Reason);
    }

    [Fact]
    public void 状態混在時は中止がなければ売上完了が優先で報告される()
    {
        var lines = new[]
        {
            NewLine(OrderStatus.FullySold, lineNumber: 1),
            NewLine(OrderStatus.PartiallySold, lineNumber: 2),
        };

        var result = OrderEditLockEvaluator.Evaluate(lines);

        Assert.True(result.IsLocked);
        Assert.Contains("売上完了", result.Reason);
    }

    private static OrderSlip NewLine(OrderStatus orderStatus, short lineNumber = 1) => new()
    {
        OrderSlipNumber = "00000001",
        LineNumber = lineNumber,
        OrderDate = DateOnly.FromDateTime(DateTime.Today),
        CustomerCode = "CUS001",
        CustomerName = "テスト得意先",
        ProductCode = "PRD001",
        ProductName = "テスト商品",
        OrderQuantity = 10m,
        UnitPrice = 1000m,
        Amount = 10000m,
        CostPrice = 700m,
        TaxCategory = TaxCategory.Standard,
        TaxRate = 10m,
        AllocatedQuantity = 0m,
        OrderStatus = orderStatus,
        SalesConfirmedQuantity = orderStatus == OrderStatus.NotSold ? 0m : 5m,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };
}
