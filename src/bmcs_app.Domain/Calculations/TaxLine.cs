using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 消費税計算の入力となる1明細行。売上（<c>sales_tax_unit_*</c>）・受注（<c>order_slip</c>）の
/// いずれのエンティティからも組み立てられる最小形にするため、<c>Customer</c> や
/// <c>SalesTaxUnitBase</c> を直接受け取らない。
/// </summary>
/// <param name="TaxCategory">税種別区分のスナップショット。</param>
/// <param name="TaxRate">
/// 税率（%）のスナップショット。非課税は0。税率マスタから引き直さず、必ず明細行が保持する値を渡す
/// （<c>docs/database-schema.md</c> 2.9節のスナップショット規則を壊さないため）。
/// </param>
/// <param name="Amount">
/// 外税計算（<see cref="TaxUnit.Invoice"/> / <see cref="TaxUnit.Slip"/>）では税抜金額、
/// 内税計算（<see cref="TaxUnit.Line"/>）では税込金額。返品・値引はマイナス。
/// </param>
public readonly record struct TaxLine(TaxCategory TaxCategory, decimal TaxRate, decimal Amount);
