using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

public class SalesTaxAmountAssignerTests
{
    [Theory]
    [InlineData(RoundingType.Floor)]
    [InlineData(RoundingType.RoundHalfUp)]
    [InlineData(RoundingType.Ceiling)]
    public void 請求単位は税額を持たない(RoundingType roundingType)
    {
        var lines = new[]
        {
            NewLine(TaxUnit.Invoice, TaxCategory.Standard, 10m, 1000m),
            NewLine(TaxUnit.Invoice, TaxCategory.Reduced, 8m, 500m),
        };

        SalesTaxAmountAssigner.Assign(lines, roundingType);

        Assert.All(lines, l => Assert.Null(l.SlipTaxAmount));
        Assert.All(lines, l => Assert.Null(l.TaxAmount));
        AssertCheckConstraint(lines);
    }

    [Theory]
    [InlineData(RoundingType.Floor)]
    [InlineData(RoundingType.RoundHalfUp)]
    [InlineData(RoundingType.Ceiling)]
    public void 伝票単位は伝票全体で1回だけ計算し全行に同値を複写する(RoundingType roundingType)
    {
        var lines = new[]
        {
            NewLine(TaxUnit.Slip, TaxCategory.Standard, 10m, 1234m),
            NewLine(TaxUnit.Slip, TaxCategory.Standard, 10m, 1234m),
            NewLine(TaxUnit.Slip, TaxCategory.Reduced, 8m, 500m),
        };

        SalesTaxAmountAssigner.Assign(lines, roundingType);

        var expected = ConsumptionTaxCalculator.CalculateSlipTaxAmount(
            lines.Select(l => new TaxLine(l.TaxCategory, l.TaxRate, l.Amount)),
            roundingType);

        Assert.All(lines, l => Assert.Equal(expected, l.SlipTaxAmount));
        Assert.All(lines, l => Assert.Null(l.TaxAmount));

        // 全行が「行数×税額」の重複計上ではなく、伝票全体で1回だけ丸めた同一値を持つことを確認する
        // （CalculateSlipTaxAmount は税率ごとに対価額を合算してから1回だけ丸めるため、
        // 10%行2つを個別に丸めて2回加算した場合と丸め誤差が異なりうる。ここではまず
        // 全行が同一値であること自体を上のAssert.Allで確認済みであり、これがSUM実装なら
        // 明細行ごとに異なる値が入ってしまうため、そもそもAssert.Allで検出される）。
        AssertCheckConstraint(lines);
    }

    [Theory]
    [InlineData(RoundingType.Floor)]
    [InlineData(RoundingType.RoundHalfUp)]
    [InlineData(RoundingType.Ceiling)]
    public void 内税明細単位は行ごとに個別計算する(RoundingType roundingType)
    {
        var lines = new[]
        {
            NewLine(TaxUnit.Line, TaxCategory.Reduced, 8m, 11000m),
            NewLine(TaxUnit.Line, TaxCategory.Standard, 10m, 2200m),
        };

        SalesTaxAmountAssigner.Assign(lines, roundingType);

        foreach (var line in lines)
        {
            var expected = ConsumptionTaxCalculator.CalculateInternalTaxAmount(
                new TaxLine(line.TaxCategory, line.TaxRate, line.Amount), roundingType);
            Assert.Equal(expected, line.TaxAmount);
        }

        Assert.All(lines, l => Assert.Null(l.SlipTaxAmount));
        AssertCheckConstraint(lines);
    }

    [Fact]
    public void 内税明細単位の内税額の具体値を確認する()
    {
        // PRD002相当: 軽減8%・内税単価550・数量20 → amount 11000 → 11000×8/108 を切上げ = 815
        var lines = new[] { NewLine(TaxUnit.Line, TaxCategory.Reduced, 8m, 11000m) };

        SalesTaxAmountAssigner.Assign(lines, RoundingType.Ceiling);

        Assert.Equal(815m, lines[0].TaxAmount);
    }

    [Fact]
    public void 非課税行は内税明細単位でも税額ゼロでNULLではない()
    {
        var lines = new[] { NewLine(TaxUnit.Line, TaxCategory.TaxExempt, 0m, 1000m) };

        SalesTaxAmountAssigner.Assign(lines, RoundingType.Floor);

        Assert.NotNull(lines[0].TaxAmount);
        Assert.Equal(0m, lines[0].TaxAmount);
    }

    [Fact]
    public void 全行非課税の伝票単位は伝票税額ゼロでNULLではない()
    {
        var lines = new[]
        {
            NewLine(TaxUnit.Slip, TaxCategory.TaxExempt, 0m, 1000m),
            NewLine(TaxUnit.Slip, TaxCategory.TaxExempt, 0m, 2000m),
        };

        SalesTaxAmountAssigner.Assign(lines, RoundingType.Floor);

        Assert.All(lines, l => Assert.NotNull(l.SlipTaxAmount));
        Assert.All(lines, l => Assert.Equal(0m, l.SlipTaxAmount));
    }

    [Fact]
    public void 空の明細行は例外()
    {
        Assert.Throws<ArgumentException>(() => SalesTaxAmountAssigner.Assign([], RoundingType.Floor));
    }

    [Fact]
    public void 税区分が混在すると例外()
    {
        var lines = new[]
        {
            NewLine(TaxUnit.Invoice, TaxCategory.Standard, 10m, 1000m),
            NewLine(TaxUnit.Slip, TaxCategory.Standard, 10m, 1000m),
        };

        Assert.Throws<ArgumentException>(() => SalesTaxAmountAssigner.Assign(lines, RoundingType.Floor));
    }

    /// <summary>
    /// DDLのCHECK制約（CK_sales_tax_amount_by_tax_unit）をテスト側に写し取ったアサーション。
    /// tax_unit=Slip のときだけSlipTaxAmountがNOT NULL、tax_unit=Lineのときだけ
    /// TaxAmountがNOT NULLであることを、全ケースから共通で検証する。
    /// </summary>
    private static void AssertCheckConstraint(IReadOnlyList<Sales> lines)
    {
        foreach (var line in lines)
        {
            Assert.Equal(line.TaxUnit == TaxUnit.Slip, line.SlipTaxAmount is not null);
            Assert.Equal(line.TaxUnit == TaxUnit.Line, line.TaxAmount is not null);
        }
    }

    private static Sales NewLine(TaxUnit taxUnit, TaxCategory taxCategory, decimal taxRate, decimal amount) => new()
    {
        SalesSlipNumber = "00000001",
        LineNumber = 1,
        SlipDate = DateOnly.FromDateTime(DateTime.Today),
        CustomerCode = "CUS001",
        TaxUnit = taxUnit,
        CustomerName = "テスト得意先",
        SlipType = SlipType.Sales,
        ProductCode = "PRD001",
        ProductName = "テスト商品",
        Quantity = 1m,
        UnitPrice = amount,
        Amount = amount,
        CostPrice = 0m,
        TaxCategory = taxCategory,
        TaxRate = taxRate,
        DeliveryNoteIssueCount = 0,
        BillingStatus = BillingLinkStatus.Unbilled,
        SettlementStatus = SettlementStatus.Unsettled,
        SettledAmount = 0m,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };
}
