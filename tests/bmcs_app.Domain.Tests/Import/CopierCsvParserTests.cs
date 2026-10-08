using System.Text;
using bmcs_app.Domain.Import;

namespace bmcs_app.Domain.Tests.Import;

public class CopierCsvParserTests
{
    private const string Header = "SOコード,SO名（漢字）,THコード,TH名（漢字）,TSコード,TS名（漢字）,一括請求先コード,一括請求先名（漢字）,LDコード,LD名（漢字）,セットNO,機種名（漢字）,機種略号,機番,法人格前後区分,法人格コード,法人格,お客様名（漢字）,事業所名（漢字）,設置部課所名（漢字）,設置部課担当者名,締日,請求訂正前締日,今回カウンタ（モノ）,今回カウンタ（フル）,今回カウンタ（フルＰ）,前回カウンタ（モノ）,前回カウンタ（フル）,前回カウンタ（フルＰ）,ＰＰ交換後カウンタ（モノ）,ＰＰ交換後カウンタ（フル）,ＰＰ交換後カウンタ（フルＰ）,ＰＰ交換前カウンタ（モノ）,ＰＰ交換前カウンタ（フル）,ＰＰ交換前カウンタ（フルＰ）,ユーザ請求CV（モノ）,ユーザ請求CV（フル）,ユーザ請求CV（フルＰ）,ユーザー請求金額（機器合計）-税別,ユーザー請求金額（機器合計）-消費税,ユーザー請求金額（機器合計）-税込,TS店売上金額-税別,TS店売上-消費税,TS店売上金額-税込,TS手数料,TS手数料消費税,一括請求店売上金額-税別,一括請求店売上-消費税,一括請求店売上金額-税込,LD店売上金額-税別,LD店売上-消費税,LD店売上金額-税込,T-P/Cトナー戻金額（機器合計）-税別,T-P/Cトナー戻金額（機器合計）-消費税,T-P/Cトナー戻金額（機器合計）-税込,S-P/Cトナー戻金額（機器合計）-税別,RIDコード,料金方式,トナー別込区分,基本枚数,支課所コード,翌日計上F,RSフラグ,請求期間,第二原図控除 CV,普通紙控除 CV,指定外控除 CV,売上確認フラグ,TS手数料得意先コード,TS手数料率,MS区分,計上品種コード,計上品種名称";

    private static readonly string[] Rows =
    [
        "G831201,リコージャパン㈱山形ＢＰ１Ｇ（山形）,,,4231400,石山商店,,,,,,ＭＰＣ６５０３,300A,110348,,99,,河北町役場,総務課,,,20260920,00000000,2276061,340722,123673,2259405,339187,122555,0,0,0,0,0,0,16489,412,1106,43249,4324,47573,38924,3892,42816,0,0,0,0,0,0,0,0,0,0,0,0,00,1,2,0,G83,0,,1,0,0,0,,,0,,,",
        "G831201,リコージャパン㈱山形ＢＰ１Ｇ（山形）,,,4231400,石山商店,,,,,,ＩＭＣ２５００,301B,632318,1,02,(有),堀米建設,,,,20260920,00000000,32827,8976,6355,32648,8952,6331,0,0,0,0,0,0,177,0,23,2240,224,2464,1980,198,2178,0,0,0,0,0,0,0,0,0,0,0,0,00,T,2,0,G83,0,C,1,0,0,0,,,0,,,",
        "G831201,リコージャパン㈱山形ＢＰ１Ｇ（山形）,,,4231400,石山商店,,,,,,ＩＭＣ２５００,301B,649346,,00,,谷地八幡宮,,,,20260920,00000000,132794,15521,10736,130439,15365,10593,0,0,0,0,0,0,2331,12,141,14555,1455,16010,12938,1293,14231,0,0,0,0,0,0,0,0,0,0,0,0,00,T,2,0,G83,0,A,1,0,0,0,,,0,,,",
        "G831201,リコージャパン㈱山形ＢＰ１Ｇ（山形）,,,4231400,石山商店,,,,,,ＩＭＣ２５００,301B,666176,1,01,(株),一品堂酒類販売,山形支店,,,20260920,00000000,88758,23474,21430,86605,23290,21264,0,0,0,0,0,0,2131,17,164,7478,747,8225,6730,673,7403,0,0,0,0,0,0,0,0,0,0,0,0,00,T,2,0,G83,0,A,1,0,0,0,,,0,,,",
    ];

    private static readonly string[] Cols = Header.Split(',');

    /// <summary>サンプル1行目の指定列だけを置き換えた行を返す（列名で指定）。</summary>
    private static string Row(params (string Col, string Value)[] edits)
    {
        var cells = Rows[0].Split(',');
        foreach (var (col, value) in edits) cells[Array.IndexOf(Cols, col)] = value;
        return string.Join(",", cells);
    }

    private static string Csv(params string[] dataLines) => string.Join("\r\n", [Header, .. dataLines]) + "\r\n";

    [Fact]
    public void Sample_ParsesAllFourRows()
    {
        var r = CopierCsvParser.Parse(Csv(Rows));

        Assert.Null(r.FileError);
        Assert.Equal(4, r.Lines.Count);
        Assert.All(r.Lines, l => Assert.NotNull(l.Row));

        var first = r.Lines[0].Row!;
        Assert.Equal(2, first.LineNumber);
        Assert.Equal("110348", first.MachineNo);
        Assert.Equal("ＭＰＣ６５０３", first.ModelName);
        Assert.Equal(43249m, first.AmountExcludingTax);
        Assert.Equal(new DateOnly(2026, 9, 20), first.ClosingDate);
        Assert.Equal("ＭＰＣ６５０３ モノ16489 フル412 フルP1106", first.SlipRemarks);
        Assert.Equal("110348", first.InternalRemarks);

        Assert.Equal(["110348", "632318", "649346", "666176"], r.Lines.Select(l => l.Row!.MachineNo));
        Assert.Equal([43249m, 2240m, 14555m, 7478m], r.Lines.Select(l => l.Row!.AmountExcludingTax));
        Assert.Equal("ＩＭＣ２５００ モノ177 フル0 フルP23", r.Lines[1].Row!.SlipRemarks);
    }

    [Fact]
    public void MissingRequiredColumn_IsFileError()
    {
        var header = Header.Replace("締日,", "");
        var r = CopierCsvParser.Parse(header + "\r\n" + Rows[0] + "\r\n");

        Assert.NotNull(r.FileError);
        Assert.Contains("締日", r.FileError);
        Assert.Empty(r.Lines);
    }

    [Fact]
    public void HalfWidthP_InColumnName_IsMissing()
    {
        var r = CopierCsvParser.Parse(Csv(Rows[0]).Replace("ユーザ請求CV（フルＰ）", "ユーザ請求CV（フルP）"));
        Assert.Contains("ユーザ請求CV（フルＰ）", r.FileError);
    }

    [Fact]
    public void InvalidClosingDate_IsRowError_OthersContinue()
    {
        var r = CopierCsvParser.Parse(Csv(Row(("締日", "20260931")), Rows[1]));

        Assert.Null(r.Lines[0].Row);
        Assert.Contains("締日", r.Lines[0].Error);
        Assert.NotNull(r.Lines[1].Row);
    }

    [Fact]
    public void InvalidAmount_IsRowError()
    {
        var r = CopierCsvParser.Parse(Csv(Row(("ユーザー請求金額（機器合計）-税別", "12a"))));
        Assert.Contains("金額", r.Lines[0].Error);
    }

    [Fact]
    public void DecimalAmount_IsParsed()
    {
        var r = CopierCsvParser.Parse(Csv(Row(("ユーザー請求金額（機器合計）-税別", "100.5"))));
        Assert.Equal(100.5m, r.Lines[0].Row!.AmountExcludingTax);
    }

    [Fact]
    public void EmptyCv_BecomesZero()
    {
        var r = CopierCsvParser.Parse(Csv(Row(
            ("ユーザ請求CV（モノ）", ""), ("ユーザ請求CV（フル）", ""), ("ユーザ請求CV（フルＰ）", ""))));
        Assert.Equal("ＭＰＣ６５０３ モノ0 フル0 フルP0", r.Lines[0].Row!.SlipRemarks);
    }

    [Fact]
    public void EmptyMachineNo_IsRowError()
    {
        var r = CopierCsvParser.Parse(Csv(Row(("機番", " "))));
        Assert.Contains("機番", r.Lines[0].Error);
    }

    [Fact]
    public void TooFewColumns_IsRowError()
    {
        var r = CopierCsvParser.Parse(Csv(string.Join(",", Rows[0].Split(',').Take(40))));
        Assert.Contains("列数", r.Lines[0].Error);
    }

    [Fact]
    public void RemarksOver200_IsRowError()
    {
        var r = CopierCsvParser.Parse(Csv(
            Row(("機種名（漢字）", new string('Ａ', 190))),
            Row(("機種名（漢字）", new string('Ａ', 150)))));

        Assert.Contains("社外摘要", r.Lines[0].Error);
        Assert.NotNull(r.Lines[1].Row);
    }

    [Fact]
    public void InternalRemarksOver200_IsRowError()
    {
        var r = CopierCsvParser.Parse(Csv(Row(("機番", new string('1', 201)))));
        Assert.Contains("社内摘要", r.Lines[0].Error);
    }

    [Fact]
    public void ColumnOrder_DoesNotMatter()
    {
        var perm = Enumerable.Range(0, Cols.Length).Reverse().ToArray();
        string Reorder(string line) { var c = line.Split(','); return string.Join(",", perm.Select(i => c[i])); }

        var a = CopierCsvParser.Parse(Csv(Rows));
        var b = CopierCsvParser.Parse(string.Join("\r\n", new[] { Reorder(Header) }.Concat(Rows.Select(Reorder))));

        Assert.Equal(a.Lines.Select(l => l.Row), b.Lines.Select(l => l.Row));
    }

    [Fact]
    public void BlankLines_AreIgnored_ButLineNumbersKept()
    {
        var r = CopierCsvParser.Parse(Header + "\r\n\r\n" + Rows[0] + "\r\n,,,\r\n  \r\n" + Rows[1]);

        Assert.Equal(2, r.Lines.Count);
        Assert.Equal([3, 6], r.Lines.Select(l => l.LineNumber));
    }

    [Fact]
    public void Values_AreTrimmed()
    {
        var r = CopierCsvParser.Parse(Csv(Row(
            ("機番", " 110348 "), ("機種名（漢字）", " ＭＰＣ６５０３ "),
            ("ユーザ請求CV（モノ）", " 5 "), ("ユーザー請求金額（機器合計）-税別", " 100 "), ("締日", " 20260920 "))));

        var row = r.Lines[0].Row!;
        Assert.Equal("110348", row.MachineNo);
        Assert.Equal("ＭＰＣ６５０３ モノ5 フル412 フルP1106", row.SlipRemarks);
        Assert.Equal(100m, row.AmountExcludingTax);
    }

    [Fact]
    public void ShiftJis_CanBeDecoded()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding("shift_jis");
        const string text = "機種名（漢字）,ＭＰＣ６５０３";

        Assert.Equal(text, sjis.GetString(sjis.GetBytes(text)));
    }
}
