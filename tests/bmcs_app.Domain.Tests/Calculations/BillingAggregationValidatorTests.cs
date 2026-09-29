using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

/// <summary>親子請求（請求集約）のリンク可否判定（2026-09-29確定）のテスト。</summary>
public class BillingAggregationValidatorTests
{
    [Fact]
    public void 自分自身を指す場合は常に許可()
    {
        var candidate = NewCustomer("CUS001", TaxUnit.Line, closingDay: 0, RoundingType.Floor, billingCustomerCode: "CUS001");

        var reason = BillingAggregationValidator.Validate(candidate, billingCustomer: null);

        Assert.Null(reason);
    }

    [Fact]
    public void 都度得意先は自分以外を請求得意先コードに指定できない()
    {
        var candidate = NewCustomer("CUS003", TaxUnit.Line, closingDay: 0, RoundingType.Floor, billingCustomerCode: "CUS001");
        var billingCustomer = NewCustomer("CUS001", TaxUnit.Invoice, closingDay: 20, RoundingType.Floor, billingCustomerCode: "CUS001");

        var reason = BillingAggregationValidator.Validate(candidate, billingCustomer);

        Assert.NotNull(reason);
    }

    [Fact]
    public void 請求得意先コードの得意先が存在しない場合は拒否()
    {
        var candidate = NewCustomer("CUS002", TaxUnit.Invoice, closingDay: 20, RoundingType.Floor, billingCustomerCode: "CUS999");

        var reason = BillingAggregationValidator.Validate(candidate, billingCustomer: null);

        Assert.NotNull(reason);
    }

    [Fact]
    public void 請求得意先コードの得意先自身が請求集約元の場合は拒否_孫は不可()
    {
        var candidate = NewCustomer("CUS003", TaxUnit.Invoice, closingDay: 20, RoundingType.Floor, billingCustomerCode: "CUS002");
        // CUS002 自身が CUS001 を請求得意先コードに指定している（＝請求集約元）。
        var billingCustomer = NewCustomer("CUS002", TaxUnit.Invoice, closingDay: 20, RoundingType.Floor, billingCustomerCode: "CUS001");

        var reason = BillingAggregationValidator.Validate(candidate, billingCustomer);

        Assert.NotNull(reason);
    }

    [Fact]
    public void 締め日が異なる請求集約先は拒否()
    {
        var candidate = NewCustomer("CUS003", TaxUnit.Invoice, closingDay: 15, RoundingType.Floor, billingCustomerCode: "CUS001");
        var billingCustomer = NewCustomer("CUS001", TaxUnit.Invoice, closingDay: 20, RoundingType.Floor, billingCustomerCode: "CUS001");

        var reason = BillingAggregationValidator.Validate(candidate, billingCustomer);

        Assert.NotNull(reason);
    }

    [Fact]
    public void 税区分が異なる請求集約先は拒否()
    {
        var candidate = NewCustomer("CUS003", TaxUnit.Slip, closingDay: 20, RoundingType.Floor, billingCustomerCode: "CUS001");
        var billingCustomer = NewCustomer("CUS001", TaxUnit.Invoice, closingDay: 20, RoundingType.Floor, billingCustomerCode: "CUS001");

        var reason = BillingAggregationValidator.Validate(candidate, billingCustomer);

        Assert.NotNull(reason);
    }

    [Fact]
    public void 端数区分が異なる請求集約先は拒否()
    {
        var candidate = NewCustomer("CUS003", TaxUnit.Invoice, closingDay: 20, RoundingType.RoundHalfUp, billingCustomerCode: "CUS001");
        var billingCustomer = NewCustomer("CUS001", TaxUnit.Invoice, closingDay: 20, RoundingType.Floor, billingCustomerCode: "CUS001");

        var reason = BillingAggregationValidator.Validate(candidate, billingCustomer);

        Assert.NotNull(reason);
    }

    [Fact]
    public void 締め日税区分端数区分がすべて一致する請求集約先は許可()
    {
        var candidate = NewCustomer("CUS003", TaxUnit.Invoice, closingDay: 20, RoundingType.Floor, billingCustomerCode: "CUS001");
        var billingCustomer = NewCustomer("CUS001", TaxUnit.Invoice, closingDay: 20, RoundingType.Floor, billingCustomerCode: "CUS001");

        var reason = BillingAggregationValidator.Validate(candidate, billingCustomer);

        Assert.Null(reason);
    }

    private static Customer NewCustomer(
        string code, TaxUnit taxUnit, byte closingDay, RoundingType roundingType, string billingCustomerCode) => new()
    {
        CustomerCode = code,
        CustomerName = $"テスト得意先{code}",
        ClosingDay = closingDay,
        TaxUnit = taxUnit,
        RoundingType = roundingType,
        PrintRepresentativeFlag = false,
        BillingCustomerCode = billingCustomerCode,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };
}
