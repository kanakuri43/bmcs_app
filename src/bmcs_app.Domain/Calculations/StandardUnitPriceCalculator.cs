using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// <see cref="IUnitPriceCalculator"/> の現時点の実装（スタブ）。内税明細単位（<see cref="TaxUnit.Line"/>）は
/// 税込単価、それ以外（請求単位／伝票単位）は税抜単価をそのまま使う（<see cref="Product"/> のXMLコメント、
/// docs/database-schema.md 参照）。掛け率マスタ等を実装する際は、本クラスを差し替える（M-3）。
/// </summary>
public sealed class StandardUnitPriceCalculator : IUnitPriceCalculator
{
    public decimal SelectStandardUnitPrice(Product product, TaxUnit taxUnit) => taxUnit switch
    {
        TaxUnit.Invoice or TaxUnit.Slip => product.StandardUnitPriceExclTax,
        TaxUnit.Line => product.StandardUnitPriceInclTax,
        _ => throw new ArgumentOutOfRangeException(nameof(taxUnit), taxUnit, "未対応の税区分です。"),
    };
}
