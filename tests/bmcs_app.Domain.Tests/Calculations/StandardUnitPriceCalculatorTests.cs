using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

public class StandardUnitPriceCalculatorTests
{
    private readonly IUnitPriceCalculator _calculator = new StandardUnitPriceCalculator();

    [Theory]
    [InlineData(TaxUnit.Invoice)]
    [InlineData(TaxUnit.Slip)]
    public void 請求単位と伝票単位は税抜単価(TaxUnit taxUnit)
    {
        var product = NewProduct(exclTax: 1000m, inclTax: 1100m);

        Assert.Equal(1000m, _calculator.SelectStandardUnitPrice(product, taxUnit));
    }

    [Fact]
    public void 内税明細単位は税込単価()
    {
        var product = NewProduct(exclTax: 1000m, inclTax: 1100m);

        Assert.Equal(1100m, _calculator.SelectStandardUnitPrice(product, TaxUnit.Line));
    }

    [Fact]
    public void 非課税商品は税抜税込が同額なのでどちらの税区分でも同じ値()
    {
        var product = NewProduct(exclTax: 1000m, inclTax: 1000m);

        Assert.Equal(1000m, _calculator.SelectStandardUnitPrice(product, TaxUnit.Invoice));
        Assert.Equal(1000m, _calculator.SelectStandardUnitPrice(product, TaxUnit.Line));
    }

    [Fact]
    public void 未対応の税区分は例外()
    {
        var product = NewProduct(exclTax: 1000m, inclTax: 1100m);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _calculator.SelectStandardUnitPrice(product, (TaxUnit)99));
    }

    private static Product NewProduct(decimal exclTax, decimal inclTax) => new()
    {
        ProductCode = "PRD001",
        ProductName = "テスト商品",
        StandardUnitPriceExclTax = exclTax,
        StandardUnitPriceInclTax = inclTax,
        StandardCostPrice = 700m,
        TaxCategory = TaxCategory.Standard,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };
}
