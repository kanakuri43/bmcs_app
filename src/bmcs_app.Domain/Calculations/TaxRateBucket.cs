using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 税率ごとに1回だけ端数処理した結果の1区分。
/// (<see cref="TaxCategory"/>, <see cref="TaxRate"/>) のペアで区分する
/// （税率改定期間をまたぐと、軽減税率と旧標準税率が同じ税率になることがあるため、
/// 税率だけでは区分できない）。
/// </summary>
/// <param name="TaxCategory">税種別区分。</param>
/// <param name="TaxRate">税率（%）。</param>
/// <param name="TaxableAmount">対価の額（常に税抜）。</param>
/// <param name="TaxAmount">端数処理済みの消費税額（1円単位）。</param>
public readonly record struct TaxRateBucket(
    TaxCategory TaxCategory,
    decimal TaxRate,
    decimal TaxableAmount,
    decimal TaxAmount);
