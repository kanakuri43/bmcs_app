using bmcs_app.Domain.Calculations;

namespace bmcs_app.Domain.Tests.Calculations;

public class InvoiceReportRowBuilderTests
{
    [Fact]
    public void 明細0件なら空を返す()
    {
        var rows = InvoiceReportRowBuilder.Build([]);

        Assert.Empty(rows);
    }

    [Fact]
    public void 単独得意先なら見出し行小計行を挟まず明細のみを返す()
    {
        var rows = InvoiceReportRowBuilder.Build(
        [
            ("CUS001", "株式会社山田商事", 1000m),
            ("CUS001", "株式会社山田商事", 2000m),
        ]);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(InvoiceReportRowKind.Line, r.Kind));
        Assert.Equal(0, rows[0].SourceLineIndex);
        Assert.Equal(1, rows[1].SourceLineIndex);
    }

    [Fact]
    public void 複数得意先が混在する場合は得意先ごとに見出し行明細行小計行の順で並ぶ()
    {
        var rows = InvoiceReportRowBuilder.Build(
        [
            ("CUS001", "株式会社山田商事", 6000m),
            ("CUS004", "株式会社山田商事　大阪支店", 3000m),
            ("CUS004", "株式会社山田商事　大阪支店", 1000m),
        ]);

        Assert.Equal(7, rows.Count);

        Assert.Equal(InvoiceReportRowKind.GroupHeader, rows[0].Kind);
        Assert.Equal("【CUS001 株式会社山田商事】", rows[0].Label);

        Assert.Equal(InvoiceReportRowKind.Line, rows[1].Kind);
        Assert.Equal(0, rows[1].SourceLineIndex);

        Assert.Equal(InvoiceReportRowKind.GroupSubtotal, rows[2].Kind);
        Assert.Equal("株式会社山田商事 小計", rows[2].Label);
        Assert.Equal(6000m, rows[2].SubtotalAmount);

        Assert.Equal(InvoiceReportRowKind.GroupHeader, rows[3].Kind);
        Assert.Equal("【CUS004 株式会社山田商事　大阪支店】", rows[3].Label);

        Assert.Equal(InvoiceReportRowKind.Line, rows[4].Kind);
        Assert.Equal(1, rows[4].SourceLineIndex);

        Assert.Equal(InvoiceReportRowKind.Line, rows[5].Kind);
        Assert.Equal(2, rows[5].SourceLineIndex);

        Assert.Equal(InvoiceReportRowKind.GroupSubtotal, rows[6].Kind);
        Assert.Equal("株式会社山田商事　大阪支店 小計", rows[6].Label);
        Assert.Equal(4000m, rows[6].SubtotalAmount);
    }

    [Fact]
    public void 返品行のマイナス金額も小計に反映される()
    {
        var rows = InvoiceReportRowBuilder.Build(
        [
            ("CUS001", "株式会社山田商事", 10000m),
            ("CUS004", "株式会社山田商事　大阪支店", 5000m),
            ("CUS004", "株式会社山田商事　大阪支店", -2000m),
        ]);

        var subtotal = Assert.Single(rows, r => r.Kind == InvoiceReportRowKind.GroupSubtotal
            && r.Label == "株式会社山田商事　大阪支店 小計");
        Assert.Equal(3000m, subtotal.SubtotalAmount);
    }

    [Fact]
    public void 同じ得意先の行が連続していない場合は別ブロックとして扱う()
    {
        // 呼び出し元が得意先コード順に整列していない異常系。クラッシュせず、非連続のまま
        // 別々の見出し行小計行ブロックとして扱われることを確認する（事前条件違反時の縮退動作）。
        var rows = InvoiceReportRowBuilder.Build(
        [
            ("CUS001", "株式会社山田商事", 1000m),
            ("CUS004", "株式会社山田商事　大阪支店", 500m),
            ("CUS001", "株式会社山田商事", 2000m),
        ]);

        Assert.Equal(9, rows.Count);
        Assert.Equal(2, rows.Count(r => r.Kind == InvoiceReportRowKind.GroupHeader && r.Label == "【CUS001 株式会社山田商事】"));
    }
}
