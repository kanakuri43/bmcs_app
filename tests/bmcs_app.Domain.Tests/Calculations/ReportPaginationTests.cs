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
}
