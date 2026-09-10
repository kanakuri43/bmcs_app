using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>売上の編集ロック（C-6の3条件。TODO.md 5-6）の判定テスト。</summary>
public class SalesEditLockEvaluatorTests
{
    [Fact]
    public void 該当なしなら編集可能()
    {
        var lines = new[] { NewLine(billingNumber: null, settlementStatus: SettlementStatus.Unsettled) };

        var result = SalesEditLockEvaluator.Evaluate(lines, monthlyClosingConfirmed: false);

        Assert.False(result.IsLocked);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void 条件1_請求締め済みなら編集不可()
    {
        var lines = new[] { NewLine(billingNumber: "BIL00000001", settlementStatus: SettlementStatus.Unsettled) };

        var result = SalesEditLockEvaluator.Evaluate(lines, monthlyClosingConfirmed: false);

        Assert.True(result.IsLocked);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void 条件2_月次締め確定なら編集不可()
    {
        var lines = new[] { NewLine(billingNumber: null, settlementStatus: SettlementStatus.Unsettled) };

        var result = SalesEditLockEvaluator.Evaluate(lines, monthlyClosingConfirmed: true);

        Assert.True(result.IsLocked);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void 条件3_消込完了なら編集不可()
    {
        var lines = new[] { NewLine(billingNumber: null, settlementStatus: SettlementStatus.FullySettled) };

        var result = SalesEditLockEvaluator.Evaluate(lines, monthlyClosingConfirmed: false);

        Assert.True(result.IsLocked);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void 一部消込は条件3に該当せず編集可能()
    {
        var lines = new[] { NewLine(billingNumber: null, settlementStatus: SettlementStatus.PartiallySettled) };

        var result = SalesEditLockEvaluator.Evaluate(lines, monthlyClosingConfirmed: false);

        Assert.False(result.IsLocked);
    }

    [Fact]
    public void 複数条件が同時に該当しても編集不可()
    {
        var lines = new[] { NewLine(billingNumber: "BIL00000001", settlementStatus: SettlementStatus.FullySettled) };

        var result = SalesEditLockEvaluator.Evaluate(lines, monthlyClosingConfirmed: true);

        Assert.True(result.IsLocked);
    }

    [Fact]
    public void 明細行のいずれか1行でも消込完了なら伝票全体が編集不可()
    {
        var lines = new[]
        {
            NewLine(billingNumber: null, settlementStatus: SettlementStatus.Unsettled),
            NewLine(billingNumber: null, settlementStatus: SettlementStatus.FullySettled),
        };

        var result = SalesEditLockEvaluator.Evaluate(lines, monthlyClosingConfirmed: false);

        Assert.True(result.IsLocked);
    }

    private static Sales NewLine(string? billingNumber, SettlementStatus settlementStatus) => new()
    {
        SalesSlipNumber = "00000001",
        LineNumber = 1,
        SlipDate = DateOnly.FromDateTime(DateTime.Today),
        CustomerCode = "CUS001",
        TaxUnit = TaxUnit.Invoice,
        CustomerName = "テスト得意先",
        SlipType = SlipType.Sales,
        ProductCode = "PRD001",
        ProductName = "テスト商品",
        Quantity = 1m,
        UnitPrice = 1000m,
        Amount = 1000m,
        CostPrice = 700m,
        TaxCategory = TaxCategory.Standard,
        TaxRate = 10m,
        DeliveryNoteIssueCount = 0,
        BillingStatus = billingNumber is null ? BillingLinkStatus.Unbilled : BillingLinkStatus.Billed,
        SettlementStatus = settlementStatus,
        SettledAmount = 0m,
        BillingNumber = billingNumber,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };
}
