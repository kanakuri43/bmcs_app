using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 商品マスタの単価を、得意先の税区分に応じて伝票明細へ転記する初期値として選ぶ（TODO.md 4-2）。
/// 内税明細単位（<see cref="TaxUnit.Line"/>）は税込単価、それ以外（請求単位／伝票単位）は
/// 税抜単価を使う（<see cref="Product"/> のXMLコメント、docs/database-schema.md 参照）。
/// </summary>
public static class UnitPriceSelector
{
    public static decimal SelectStandardUnitPrice(Product product, TaxUnit taxUnit) => taxUnit switch
    {
        TaxUnit.Invoice or TaxUnit.Slip => product.StandardUnitPriceExclTax,
        TaxUnit.Line => product.StandardUnitPriceInclTax,
        _ => throw new ArgumentOutOfRangeException(nameof(taxUnit), taxUnit, "未対応の税区分です。"),
    };
}
