using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 売上明細行（<see cref="Sales"/>）に、得意先の税区分（<see cref="TaxUnit"/>）に応じた
/// 税額カラム（<see cref="Sales.SlipTaxAmount"/> / <see cref="Sales.TaxAmount"/>）を書き込む。
///
/// <see cref="ConsumptionTaxCalculator"/> は「税率をどう丸めるか」の計算ロジックそのものであり、
/// 「税単位で分岐する単一の入口は用意しない」と既に決めている（同クラスのXMLコメント参照）。
/// 本クラスはそれとは責務が異なる——「<c>sales</c> のどの列に値を入れるか」という
/// 永続化側の対応表であり、DB の CHECK 制約
/// （<c>CK_sales_tax_amount_by_tax_unit</c> / <c>CK_sales_billing_number_by_tax_unit</c>、
/// <c>scripts/010_unify_tax_unit_tables.sql</c>）を満たすことだけを目的とする。
///
/// 同じ分岐は受注からの売上確定・返品値引・複写入力・訂正でも
/// 再登場するため、`sales` への書き込み口である <c>SalesService</c> から必ずこのクラスを
/// 経由させることで、後続フェーズも自動的に正しくなるようにする。
///
/// <see cref="Sales.BillingNumber"/> は本クラスでは扱わない。新規登録時は常に未請求
/// （<c>null</c>）であり、既存の請求済み売上を再保存する経路（訂正）で誤って請求紐付けを
/// 消してしまわないよう、呼び出し元（新規登録専用の <c>SalesService.CreateAsync</c>）の
/// 責務とする。
/// </summary>
public static class SalesTaxAmountAssigner
{
    public static void Assign(IReadOnlyList<Sales> slipLines, RoundingType roundingType)
    {
        if (slipLines.Count == 0)
        {
            throw new ArgumentException("売上明細行が1件もありません。", nameof(slipLines));
        }

        var taxUnit = slipLines[0].TaxUnit;
        if (slipLines.Any(line => line.TaxUnit != taxUnit))
        {
            throw new ArgumentException("同一伝票の明細行に異なる税区分が混在しています。", nameof(slipLines));
        }

        switch (taxUnit)
        {
            case TaxUnit.Invoice:
                // 請求単位: 請求締め時に一括計算するため、伝票時点では税額を持たない。
                foreach (var line in slipLines)
                {
                    line.SlipTaxAmount = null;
                    line.TaxAmount = null;
                }
                break;

            case TaxUnit.Slip:
                // 伝票単位: 伝票全体で1回だけ税額を確定し、同一値を全行に複写する（SUMしない）。
                var slipTaxAmount = ConsumptionTaxCalculator.CalculateSlipTaxAmount(
                    slipLines.Select(ToTaxLine),
                    roundingType);
                foreach (var line in slipLines)
                {
                    line.SlipTaxAmount = slipTaxAmount;
                    line.TaxAmount = null;
                }
                break;

            case TaxUnit.Line:
                // 内税明細単位: 明細行ごとに内税額を確定する（Amount は既に税込金額）。
                foreach (var line in slipLines)
                {
                    line.SlipTaxAmount = null;
                    line.TaxAmount = ConsumptionTaxCalculator.CalculateInternalTaxAmount(ToTaxLine(line), roundingType);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(slipLines), taxUnit, "未対応の税区分です。");
        }
    }

    private static TaxLine ToTaxLine(Sales line) => new(line.TaxCategory, line.TaxRate, line.Amount);
}
