namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 帳票の明細行を複数ページに分割する（TODO.md 10-3）。
/// 1ページ目と続紙でヘッダー高さが異なる帳票が多いため、収容行数を別々に受け取る。
/// 実際のページ組み立て（<see cref="System.Windows.Documents.FixedPage"/>）は WPF に依存するため
/// Presentation 層（<c>src/bmcs_app/Reports/</c>）に置くが、この分割計算自体は純粋関数として
/// Domain に置き、単体テストで境界値を保証する。
/// </summary>
public static class ReportPagination
{
    /// <summary>
    /// 明細総数を、1ページ目 <paramref name="linesOnFirstPage"/> 行・以降のページ
    /// <paramref name="linesOnLaterPages"/> 行ずつのチャンクに分割する。
    /// 明細が0件でもヘッダー・フッターだけのページを1枚返す。
    /// </summary>
    public static IReadOnlyList<(int StartIndex, int Count)> Split(
        int totalLineCount, int linesOnFirstPage, int linesOnLaterPages)
    {
        if (totalLineCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLineCount), totalLineCount, "明細総数は0以上である必要があります。");
        }

        if (linesOnFirstPage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(linesOnFirstPage), linesOnFirstPage, "1ページ目の収容行数は1以上である必要があります。");
        }

        if (linesOnLaterPages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(linesOnLaterPages), linesOnLaterPages, "続紙の収容行数は1以上である必要があります。");
        }

        var pages = new List<(int StartIndex, int Count)>();

        if (totalLineCount == 0)
        {
            pages.Add((0, 0));
            return pages;
        }

        var firstCount = Math.Min(totalLineCount, linesOnFirstPage);
        pages.Add((0, firstCount));

        var remaining = totalLineCount - firstCount;
        var index = firstCount;

        while (remaining > 0)
        {
            var count = Math.Min(remaining, linesOnLaterPages);
            pages.Add((index, count));
            index += count;
            remaining -= count;
        }

        return pages;
    }

    /// <summary>
    /// <see cref="Split"/>の結果を、ページ末尾に見出し行（<paramref name="isHeaderRow"/>が真を返す行）が
    /// 孤立しないよう調整する（TODO.md 12-D、親子請求の請求書帳票。docs/report-spec.md 2-2-1節）。
    /// 該当する場合、そのページの末尾から見出し行を1件切り出して次ページの先頭へ移す。
    /// 見出し行の直後には必ず1件以上の明細行と小計行が続く構造（<see cref="InvoiceReportRowBuilder"/>）
    /// のため、この調整は見出し行と次ページへ送られる行が同じページ境界にまたがることはなく、
    /// 1回の走査で十分（連鎖的な調整は発生しない）。
    /// </summary>
    public static IReadOnlyList<(int StartIndex, int Count)> AvoidTrailingHeaderOrphans(
        IReadOnlyList<(int StartIndex, int Count)> pages, Func<int, bool> isHeaderRow)
    {
        if (pages.Count <= 1)
        {
            return pages;
        }

        var adjusted = pages.ToList();
        for (var i = 0; i < adjusted.Count - 1; i++)
        {
            var (startIndex, count) = adjusted[i];
            if (count == 0)
            {
                continue;
            }

            var lastRowIndex = startIndex + count - 1;
            if (!isHeaderRow(lastRowIndex))
            {
                continue;
            }

            adjusted[i] = (startIndex, count - 1);
            var (nextStart, nextCount) = adjusted[i + 1];
            adjusted[i + 1] = (nextStart - 1, nextCount + 1);
        }

        return adjusted;
    }
}
