using bmcs_app.Domain.Calculations;

namespace bmcs_app.Domain.Tests.Calculations;

public class ReportPaginationTests
{
    [Fact]
    public void 明細0件でもヘッダーとフッターだけの1ページを返す()
    {
        var pages = ReportPagination.Split(totalLineCount: 0, linesOnFirstPage: 10, linesOnLaterPages: 20);

        Assert.Single(pages);
        Assert.Equal((0, 0), pages[0]);
    }

    [Fact]
    public void 全明細が1ページ目にちょうど収まる場合は1ページ()
    {
        var pages = ReportPagination.Split(totalLineCount: 10, linesOnFirstPage: 10, linesOnLaterPages: 20);

        Assert.Single(pages);
        Assert.Equal((0, 10), pages[0]);
    }

    [Fact]
    public void 先頭ページから1行溢れると2ページになる()
    {
        var pages = ReportPagination.Split(totalLineCount: 11, linesOnFirstPage: 10, linesOnLaterPages: 20);

        Assert.Equal(2, pages.Count);
        Assert.Equal((0, 10), pages[0]);
        Assert.Equal((10, 1), pages[1]);
    }

    [Fact]
    public void 続紙が複数ページにわたる()
    {
        var pages = ReportPagination.Split(totalLineCount: 45, linesOnFirstPage: 10, linesOnLaterPages: 20);

        Assert.Equal(3, pages.Count);
        Assert.Equal((0, 10), pages[0]);
        Assert.Equal((10, 20), pages[1]);
        Assert.Equal((30, 15), pages[2]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 先頭ページの収容行数が0以下は例外(int linesOnFirstPage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReportPagination.Split(totalLineCount: 5, linesOnFirstPage: linesOnFirstPage, linesOnLaterPages: 20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 続紙の収容行数が0以下は例外(int linesOnLaterPages)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReportPagination.Split(totalLineCount: 5, linesOnFirstPage: 10, linesOnLaterPages: linesOnLaterPages));
    }

    [Fact]
    public void 明細総数が負なら例外()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReportPagination.Split(totalLineCount: -1, linesOnFirstPage: 10, linesOnLaterPages: 20));
    }

    [Fact]
    public void ページ末尾の見出し行を次ページの先頭へ送る()
    {
        // 行9（0始まり）が見出し行。1ページ目は0〜9(10行)で見出し行がちょうど末尾に来る。
        var pages = new List<(int StartIndex, int Count)> { (0, 10), (10, 5) };

        var adjusted = ReportPagination.AvoidTrailingHeaderOrphans(pages, lineIndex => lineIndex == 9);

        Assert.Equal((0, 9), adjusted[0]);
        Assert.Equal((9, 6), adjusted[1]);
    }

    [Fact]
    public void 見出し行がページ末尾でなければ調整しない()
    {
        var pages = new List<(int StartIndex, int Count)> { (0, 10), (10, 5) };

        var adjusted = ReportPagination.AvoidTrailingHeaderOrphans(pages, lineIndex => lineIndex == 5);

        Assert.Equal(pages, adjusted);
    }

    [Fact]
    public void ページが1枚だけなら調整しない()
    {
        var pages = new List<(int StartIndex, int Count)> { (0, 10) };

        var adjusted = ReportPagination.AvoidTrailingHeaderOrphans(pages, _ => true);

        Assert.Equal(pages, adjusted);
    }

    [Fact]
    public void 明細0件のページは調整対象にならない()
    {
        var pages = new List<(int StartIndex, int Count)> { (0, 0), (0, 5) };

        var adjusted = ReportPagination.AvoidTrailingHeaderOrphans(pages, _ => true);

        Assert.Equal(pages, adjusted);
    }

    [Fact]
    public void 複数ページにまたがる見出し行の孤立をそれぞれ調整する()
    {
        // 行9・行19がそれぞれのページ末尾に来る見出し行。
        var pages = new List<(int StartIndex, int Count)> { (0, 10), (10, 10), (20, 5) };

        var adjusted = ReportPagination.AvoidTrailingHeaderOrphans(pages, lineIndex => lineIndex is 9 or 19);

        Assert.Equal((0, 9), adjusted[0]);
        Assert.Equal((9, 10), adjusted[1]);
        Assert.Equal((19, 6), adjusted[2]);
    }
}
