using bmcs_app.Domain.Entities;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 受注・売上明細へ転記する単価の初期値を決める。得意先／商品／単価マスタ → 得意先／商品／定価掛率マスタ →
/// 商品マスタ の順に採用する（docs/database-schema.md 2.2節）。マスタ行の取得と税率の解決は呼び出し側
/// （<c>UnitPriceService</c>）が行い、本インターフェースは渡された値から単価を決めるだけの純粋関数にする。
/// 「将来の差し替えを見据えた抽象化は行わない」という既定方針に対する明示的な例外として、
/// 差し替え可能なインターフェースにしている（docs/decisions.md 参照）。
/// </summary>
public interface IUnitPriceCalculator
{
    /// <param name="product">対象商品。</param>
    /// <param name="customer">対象得意先。未選択（null）のときは外税単価を使う。</param>
    /// <param name="customerPrice">この得意先・商品の単価マスタ行（無ければ null）。</param>
    /// <param name="customerRate">この得意先・商品の定価掛率マスタ行（無ければ null）。</param>
    /// <param name="taxRate">商品の税種別と伝票日付で解決した税率（%）。非課税は 0。内税得意先の掛率計算でのみ使う。</param>
    decimal SelectUnitPrice(
        Product product,
        Customer? customer,
        CustomerProductPrice? customerPrice,
        CustomerProductRate? customerRate,
        decimal taxRate);
}
