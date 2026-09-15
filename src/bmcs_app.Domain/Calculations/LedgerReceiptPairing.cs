using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 都度得意先（<see cref="TaxUnit.Line"/>）の売上明細行に対し、消込の証跡となる明細入金行
/// （<see cref="DetailReceipt"/>）を紐づける（TODO.md 8-1）。
///
/// **表示専用であり、残高計算には使わない。** 残高（<see cref="LedgerEntryKind.Receipt"/> の
/// 独立行）は明細入金行ごとに <see cref="DetailReceipt.AllocatedAmount"/> をそのまま計上する別ロジック
/// （<see cref="CustomerLedgerBuilder"/>）が担うため、本クラスは金額の按分を一切行わない
/// （<c>SettlementService</c>の消込配分とは異なる。証跡が万一不正確でも残高は狂わない設計）。
///
/// <see cref="DetailReceiptTargetType.SalesLine"/>（直接指定）はエンティティが対象を直接持つため
/// 単純な参照。<see cref="DetailReceiptTargetType.DetailInvoice"/>（明細請求書指定）は1入金が
/// 請求書まるごとを指すため、その請求書に連携する**すべて**の売上明細行に証跡として結びつける
/// （どの明細行にいくら充当されたかの厳密な按分は行わない）。
/// </summary>
public static class LedgerReceiptPairing
{
    /// <param name="detailReceiptLines">得意先の全期間の明細入金行。</param>
    /// <param name="invoiceLinks">明細請求書と売上明細行の連携（<c>detail_invoice_sales_line</c>）。</param>
    /// <returns>
    /// (売上伝票No, 行No) をキーに、証跡となる明細入金行を入金日付順に並べた辞書。
    /// キーが存在しない売上明細行は証跡なし（連携する明細請求書が取消済みの場合等）。
    /// </returns>
    public static IReadOnlyDictionary<(string SalesSlipNumber, short SalesLineNumber), IReadOnlyList<DetailReceipt>> Pair(
        IReadOnlyList<DetailReceipt> detailReceiptLines,
        IReadOnlyList<DetailInvoiceSalesLine> invoiceLinks)
    {
        var salesLinesByInvoice = invoiceLinks
            .GroupBy(l => l.DetailInvoiceNumber)
            .ToDictionary(g => g.Key, g => g.Select(l => (l.SalesSlipNumber, l.SalesLineNumber)).ToList());

        var result = new Dictionary<(string, short), List<DetailReceipt>>();

        void Add((string, short) key, DetailReceipt line)
        {
            if (!result.TryGetValue(key, out var list))
            {
                list = [];
                result[key] = list;
            }

            list.Add(line);
        }

        foreach (var line in detailReceiptLines)
        {
            if (line.TargetType == DetailReceiptTargetType.SalesLine)
            {
                Add((line.TargetSalesSlipNumber!, line.TargetSalesLineNumber!.Value), line);
            }
            else if (salesLinesByInvoice.TryGetValue(line.TargetDetailInvoiceNumber!, out var linkedSalesLines))
            {
                foreach (var key in linkedSalesLines)
                {
                    Add(key, line);
                }
            }

            // 連携する売上明細行が見つからない場合（取消済み明細請求書等）は証跡を作らない。
            // 独立行としての残高計上は別ロジックが行うため、証跡が欠けても残高は狂わない。
        }

        return result.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<DetailReceipt>)kv.Value
                .OrderBy(l => l.ReceiptDate).ThenBy(l => l.DetailReceiptNumber).ThenBy(l => l.LineNumber)
                .ToList());
    }
}
