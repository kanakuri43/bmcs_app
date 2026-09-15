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
}
