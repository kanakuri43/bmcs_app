using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 商品マスタの単価を、得意先の税区分に応じて伝票明細へ転記する初期値として選ぶ。
/// 将来的に得意先別単価・数量別単価・掛け率マスタ等を実装する予定があるため、差し替え可能な
/// インターフェースとして定義する（「将来の差し替えを見据えた抽象化は行わない」という
/// 既定方針に対する明示的な例外。docs/decisions.md 参照）。現時点の実装は <see cref="StandardUnitPriceCalculator"/>
/// のみで、単価計算マスタは使わない。
/// </summary>
public interface IUnitPriceCalculator
{
    decimal SelectStandardUnitPrice(Product product, TaxUnit taxUnit);
}
