using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// <see cref="IUnitPriceCalculator"/> の実装。得意先／商品／単価マスタの単価はそのまま、定価掛率マスタは
/// 商品マスタの定価 × 掛率（内税明細単位の得意先は税込へ換算し、最後に得意先の端数区分で1回だけ丸める）、
/// どちらも無ければ商品マスタの単価（内税明細単位は税込、それ以外は税抜）を使う。
/// </summary>
public sealed class StandardUnitPriceCalculator : IUnitPriceCalculator
{
    public decimal SelectUnitPrice(
        Product product,
        Customer? customer,
        CustomerProductPrice? customerPrice,
        CustomerProductRate? customerRate,
        decimal taxRate)
    {
        if (customer is null)
        {
            return product.StandardUnitPriceExclTax;
        }

        if (customerPrice is not null)
        {
            return customerPrice.UnitPrice;
        }

        if (customerRate is not null)
        {
            // 定価未設定の商品は、掛率があっても商品マスタ単価へは落とさず 0 にする。
            if (product.CatalogPrice == 0m)
            {
                return 0m;
            }

            var price = product.CatalogPrice * customerRate.CatalogPriceRate / 100m;
            if (customer.TaxUnit == TaxUnit.Line)
            {
                price *= 1m + taxRate / 100m;
            }

            return TaxRounding.RoundToYen(price, customer.RoundingType);
        }

        return customer.TaxUnit switch
        {
            TaxUnit.Invoice or TaxUnit.Slip => product.StandardUnitPriceExclTax,
            TaxUnit.Line => product.StandardUnitPriceInclTax,
            _ => throw new ArgumentOutOfRangeException(nameof(customer), customer.TaxUnit, "未対応の税区分です。"),
        };
    }
}
