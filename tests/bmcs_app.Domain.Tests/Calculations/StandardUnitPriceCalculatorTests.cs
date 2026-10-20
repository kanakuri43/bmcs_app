using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

public class StandardUnitPriceCalculatorTests
{
    private readonly IUnitPriceCalculator _calculator = new StandardUnitPriceCalculator();

    // ── ③ 商品マスタ ──────────────────────────────────────────

    [Fact]
    public void 得意先未選択は外税単価()
    {
        var product = NewProduct(exclTax: 1000m, inclTax: 1100m);

        Assert.Equal(1000m, _calculator.SelectUnitPrice(product, null, null, null, 10m));
    }

    [Theory]
    [InlineData(TaxUnit.Invoice)]
    [InlineData(TaxUnit.Slip)]
    public void マスタ設定なしの請求単位と伝票単位は税抜単価(TaxUnit taxUnit)
    {
        var product = NewProduct(exclTax: 1000m, inclTax: 1100m);

        Assert.Equal(1000m, _calculator.SelectUnitPrice(product, NewCustomer(taxUnit), null, null, 10m));
    }

    [Fact]
    public void マスタ設定なしの内税明細単位は税込単価()
    {
        var product = NewProduct(exclTax: 1000m, inclTax: 1100m);

        Assert.Equal(1100m, _calculator.SelectUnitPrice(product, NewCustomer(TaxUnit.Line), null, null, 10m));
    }

    [Fact]
    public void 未対応の税区分は例外()
    {
        var product = NewProduct(exclTax: 1000m, inclTax: 1100m);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _calculator.SelectUnitPrice(product, NewCustomer((TaxUnit)99), null, null, 10m));
    }

    // ── ① 得意先／商品／単価マスタ ────────────────────────────

    [Fact]
    public void 単価マスタはそのまま転記され掛率マスタより優先される()
    {
        var product = NewProduct(exclTax: 1000m, inclTax: 1100m, catalogPrice: 2000m);
        var customer = NewCustomer(TaxUnit.Line);

        var result = _calculator.SelectUnitPrice(product, customer, NewPrice(777m), NewRate(50), 10m);

        Assert.Equal(777m, result);
    }

    // ── ② 得意先／商品／定価掛率マスタ ────────────────────────

    [Fact]
    public void 外税得意先の掛率は定価に掛けるだけで税率を使わない()
    {
        var product = NewProduct(exclTax: 1000m, inclTax: 1100m, catalogPrice: 1000m);

        // 1000 × 80% = 800（税率10%を渡しても換算しない）
        Assert.Equal(800m, _calculator.SelectUnitPrice(product, NewCustomer(TaxUnit.Invoice), null, NewRate(80), 10m));
    }

    [Theory]
    [InlineData(RoundingType.Floor, 925)]
    [InlineData(RoundingType.RoundHalfUp, 926)]
    [InlineData(RoundingType.Ceiling, 926)]
    public void 掛率の端数は得意先の端数区分で丸める(RoundingType roundingType, int expected)
    {
        var product = NewProduct(exclTax: 0m, inclTax: 0m, catalogPrice: 1234m);

        // 1234 × 75% = 925.5
        var customer = NewCustomer(TaxUnit.Invoice, roundingType);
        Assert.Equal(expected, _calculator.SelectUnitPrice(product, customer, null, NewRate(75), 10m));
    }

    [Fact]
    public void 内税得意先の掛率は税込へ換算する()
    {
        var product = NewProduct(exclTax: 0m, inclTax: 0m, catalogPrice: 1000m);

        // 1000 × 80% × 1.10 = 880
        Assert.Equal(880m, _calculator.SelectUnitPrice(product, NewCustomer(TaxUnit.Line), null, NewRate(80), 10m));
    }

    [Fact]
    public void 内税得意先の換算後の端数は1回だけ丸める()
    {
        var product = NewProduct(exclTax: 0m, inclTax: 0m, catalogPrice: 999m);

        // 999 × 83% × 1.08 = 895.5036。換算前（829.17）で丸めると 829 × 1.08 = 895.32 → 895 になり、
        // 切上では 830 × 1.08 = 896.4 → 897 と食い違う。換算後に1回だけ丸めた値は切上で 896。
        Assert.Equal(895m, _calculator.SelectUnitPrice(product, NewCustomer(TaxUnit.Line, RoundingType.Floor), null, NewRate(83), 8m));
        Assert.Equal(896m, _calculator.SelectUnitPrice(product, NewCustomer(TaxUnit.Line, RoundingType.Ceiling), null, NewRate(83), 8m));
    }

    [Fact]
    public void 内税得意先でも非課税商品は税率0なので換算しない()
    {
        var product = NewProduct(exclTax: 0m, inclTax: 0m, catalogPrice: 1000m, taxCategory: TaxCategory.TaxExempt);

        Assert.Equal(800m, _calculator.SelectUnitPrice(product, NewCustomer(TaxUnit.Line), null, NewRate(80), 0m));
    }

    [Fact]
    public void 掛率0は単価0()
    {
        var product = NewProduct(exclTax: 1000m, inclTax: 1100m, catalogPrice: 1000m);

        Assert.Equal(0m, _calculator.SelectUnitPrice(product, NewCustomer(TaxUnit.Invoice), null, NewRate(0), 10m));
    }

    [Fact]
    public void 定価0の商品は掛率があっても商品マスタ単価へ落とさず0()
    {
        var product = NewProduct(exclTax: 1000m, inclTax: 1100m, catalogPrice: 0m);

        Assert.Equal(0m, _calculator.SelectUnitPrice(product, NewCustomer(TaxUnit.Invoice), null, NewRate(80), 10m));
        Assert.Equal(0m, _calculator.SelectUnitPrice(product, NewCustomer(TaxUnit.Line), null, NewRate(80), 10m));
    }

    private static CustomerProductPrice NewPrice(decimal unitPrice) => new()
    {
        CustomerCode = "CUS001",
        ProductCode = "PRD001",
        UnitPrice = unitPrice,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static CustomerProductRate NewRate(int rate) => new()
    {
        CustomerCode = "CUS001",
        ProductCode = "PRD001",
        CatalogPriceRate = rate,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static Customer NewCustomer(TaxUnit taxUnit, RoundingType roundingType = RoundingType.Floor) => new()
    {
        CustomerCode = "CUS001",
        CustomerName = "テスト得意先",
        ClosingDay = 15,
        PrintRepresentativeFlag = false,
        TaxUnit = taxUnit,
        RoundingType = roundingType,
        BillingCustomerCode = "CUS001",
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };

    private static Product NewProduct(
        decimal exclTax, decimal inclTax, decimal catalogPrice = 0m, TaxCategory taxCategory = TaxCategory.Standard) => new()
    {
        ProductCode = "PRD001",
        ProductName = "テスト商品",
        StandardUnitPriceExclTax = exclTax,
        StandardUnitPriceInclTax = inclTax,
        StandardCostPrice = 700m,
        CatalogPrice = catalogPrice,
        TaxCategory = taxCategory,
        CreatedBy = "TEST",
        CreatedAt = DateTime.Now,
        UpdatedBy = "TEST",
        UpdatedAt = DateTime.Now,
    };
}
