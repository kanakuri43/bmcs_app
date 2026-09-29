namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 請求書明細を得意先ごとに区切って表示する行順を組み立てる（TODO.md 12-D、親子請求。
/// docs/report-spec.md 2-2-1節）。請求集約先の請求書には、配下の請求集約元（支店等）の分も
/// 合算して1件の<c>billing</c>にまとめられるため、明細のどこからどこまでがどの得意先の分かを
/// 見出し行・小計行で示す。
/// </summary>
public static class InvoiceReportRowBuilder
{
    /// <summary>
    /// <paramref name="lines"/>が単独得意先1件分（＝集約されていない請求書）なら、見出し行・
    /// 小計行を挟まずそのまま返す（既存の単独得意先の帳票をバイト単位で不変に保つための分岐）。
    /// 複数得意先が混在する場合は、得意先ごとに見出し行→明細行→小計行の順で並べる。
    /// </summary>
    /// <param name="lines">
    /// 得意先コード順→（呼び出し元が決める）任意の順で整列済みであること。同じ得意先の行が
    /// 連続していない場合、その得意先の見出し・小計ブロックが複数回に分かれて出力される。
    /// </param>
    public static IReadOnlyList<InvoiceReportRow> Build(
        IReadOnlyList<(string CustomerCode, string CustomerName, decimal Amount)> lines)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        if (lines.Select(l => l.CustomerCode).Distinct().Count() <= 1)
        {
            return Enumerable.Range(0, lines.Count)
                .Select(i => new InvoiceReportRow { Kind = InvoiceReportRowKind.Line, SourceLineIndex = i })
                .ToList();
        }

        var rows = new List<InvoiceReportRow>();
        var i = 0;
        while (i < lines.Count)
        {
            var (customerCode, customerName, _) = lines[i];
            rows.Add(new InvoiceReportRow
            {
                Kind = InvoiceReportRowKind.GroupHeader,
                Label = $"【{customerCode} {customerName}】",
            });

            var subtotal = 0m;
            while (i < lines.Count && lines[i].CustomerCode == customerCode)
            {
                rows.Add(new InvoiceReportRow { Kind = InvoiceReportRowKind.Line, SourceLineIndex = i });
                subtotal += lines[i].Amount;
                i++;
            }

            rows.Add(new InvoiceReportRow
            {
                Kind = InvoiceReportRowKind.GroupSubtotal,
                Label = $"{customerName} 小計",
                SubtotalAmount = subtotal,
            });
        }

        return rows;
    }
}
