using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using bmcs_app.Domain.Calculations;

namespace bmcs_app.Reports;

/// <summary>
/// 改ページを伴う単一フロー帳票（請求書・明細請求書）の A4 <see cref="FixedDocument"/> を
/// 組み立てる基底クラス（帳票基盤 <see cref="ReportDocumentBuilder"/>
/// から分割）。派生クラスはヘッダー・フッター・列定義・
/// 行データのみを与える。改ページの分割計算そのものは <see cref="ReportPagination"/>
/// （Domain、単体テスト済み）に委ねる。
/// </summary>
public abstract class PagedReportDocumentBuilder : ReportDocumentBuilder
{
    /// <summary>A4横で出力するか（既定は縦）。横の場合、残余幅の列（幅0）は使えない（幅を明示する）。</summary>
    protected virtual bool IsLandscape => false;

    /// <summary>明細テーブルの文字サイズ。null なら既定（9pt）。</summary>
    protected virtual double? TableFontSize => null;

    private double PageWidth => IsLandscape ? A4Height : A4Width;
    private double PageHeight => IsLandscape ? A4Width : A4Height;
    private double BodyWidth => IsLandscape ? LandscapeContentWidth : ContentWidth;
    private double BodyHeight => IsLandscape ? LandscapeContentHeight : ContentHeight;

    /// <summary>1ページ目のヘッダー（タイトル・得意先・自社情報等）の高さの見積り。</summary>
    protected abstract double FullHeaderHeight { get; }

    /// <summary>2ページ目以降の続紙ヘッダーの高さの見積り。</summary>
    protected abstract double CompactHeaderHeight { get; }

    /// <summary>最終ページのフッター（税率別内訳・合計等）の高さの見積り。</summary>
    protected abstract double FooterHeight { get; }

    /// <summary>
    /// 全ページの明細テーブルの下に出すページフッターの高さの見積り（<see cref="BuildPageFooter"/>を使う帳票のみ。
    /// 既定は0＝ページフッター無し）。得意先元帳の「前頁繰越／次頁繰越」のように毎ページ必要な帳票向け。
    /// </summary>
    protected virtual double PageFooterHeight => 0;

    /// <summary>明細テーブルの列定義。幅0の列は残余幅になる（最大1列まで）。</summary>
    protected abstract IReadOnlyList<ReportColumn> Columns { get; }

    /// <summary>明細行の総数。</summary>
    protected abstract int LineCount { get; }

    /// <summary>1ページ目の先頭に出すフルヘッダー。</summary>
    protected abstract FrameworkElement BuildFullHeader(int totalPages);

    /// <summary>2ページ目以降の先頭に出す続紙ヘッダー。</summary>
    protected abstract FrameworkElement BuildCompactHeader(int pageNumber, int totalPages);

    /// <summary>最終ページの明細テーブルの下に出すフッター。</summary>
    protected abstract FrameworkElement BuildFooter();

    /// <summary>
    /// 毎ページの明細テーブルの下に出すフッター。<paramref name="startIndex"/>／<paramref name="count"/>
    /// はそのページの明細範囲。既定は null（出さない）。最終ページでは <see cref="BuildFooter"/> の上に出る。
    /// </summary>
    protected virtual FrameworkElement? BuildPageFooter(int startIndex, int count, bool isLast) => null;

    /// <summary><paramref name="lineIndex"/>（0始まり）の行を <see cref="Columns"/> と同じ順序のセル文字列で返す。</summary>
    protected abstract string?[] BuildLineCells(int lineIndex);

    /// <summary>
    /// 見出し行・小計行のような、通常の明細行とは異なる太字・単色背景で描画すべき行かどうか
    /// 。既定はfalse（全行が通常の明細行）。
    /// </summary>
    protected virtual bool IsGroupMarkerRow(int lineIndex) => false;

    /// <summary>
    /// ページ末尾に来ると孤立してしまう行（見出し行）かどうか。
    /// <see cref="ReportPagination.AvoidTrailingHeaderOrphans"/>が参照する。既定はfalse
    /// （調整不要）。
    /// </summary>
    protected virtual bool IsPageBreakSensitive(int lineIndex) => false;

    public FixedDocument Build()
    {
        var document = new FixedDocument();
        BuildInto(document);
        return document;
    }

    /// <summary>
    /// 既存の<see cref="Build"/>と同じページ組み立てを、呼び出し元が用意した<paramref name="document"/>に
    /// 追加する。
    /// ページ番号（<c>pageNumber</c>/<c>totalPages</c>）はこのビルダー分だけで1始まりに閉じる
    /// （請求書ごとに「1/N ページ」と表示するため、連結先の既存ページ数はここでは加味しない）。
    /// </summary>
    public void BuildInto(FixedDocument document)
    {
        var linesOnFirstPage = Math.Max(1,
            (int)((BodyHeight - FullHeaderHeight - TableHeaderHeight - FooterHeight - PageFooterHeight) / LineHeight));
        var linesOnLaterPages = Math.Max(1,
            (int)((BodyHeight - CompactHeaderHeight - TableHeaderHeight - FooterHeight - PageFooterHeight) / LineHeight));

        var pageSplits = ReportPagination.Split(LineCount, linesOnFirstPage, linesOnLaterPages);
        pageSplits = ReportPagination.AvoidTrailingHeaderOrphans(pageSplits, IsPageBreakSensitive);

        for (var i = 0; i < pageSplits.Count; i++)
        {
            var (startIndex, count) = pageSplits[i];
            AddPage(
                document, startIndex, count,
                isFirst: i == 0, isLast: i == pageSplits.Count - 1,
                pageNumber: i + 1, totalPages: pageSplits.Count);
        }
    }

    private void AddPage(
        FixedDocument document, int startIndex, int count,
        bool isFirst, bool isLast, int pageNumber, int totalPages)
    {
        var fixedPage = new FixedPage
        {
            Width = PageWidth,
            Height = PageHeight,
            Background = Brushes.White,
        };

        var content = BuildPageContent(startIndex, count, isFirst, isLast, pageNumber, totalPages);
        FixedPage.SetLeft(content, MarginX);
        FixedPage.SetTop(content, MarginY);
        fixedPage.Children.Add(content);

        // FixedPage は Show/ShowDialog のビジュアルツリーに乗らないため、
        // レイアウトを自分で確定させる必要がある（参考実装と同じ手順）。
        fixedPage.Measure(new Size(PageWidth, PageHeight));
        fixedPage.Arrange(new Rect(0, 0, PageWidth, PageHeight));
        fixedPage.UpdateLayout();

        var pageContent = new PageContent();
        ((IAddChild)pageContent).AddChild(fixedPage);
        document.Pages.Add(pageContent);
    }

    private FrameworkElement BuildPageContent(
        int startIndex, int count, bool isFirst, bool isLast, int pageNumber, int totalPages)
    {
        var root = new StackPanel { Width = BodyWidth, Background = Brushes.White };

        root.Children.Add(isFirst ? BuildFullHeader(totalPages) : BuildCompactHeader(pageNumber, totalPages));
        root.Children.Add(HLine(1));
        root.Children.Add(BuildLinesTable(startIndex, count));

        if (BuildPageFooter(startIndex, count, isLast) is { } pageFooter)
        {
            root.Children.Add(HLine(0.5));
            root.Children.Add(pageFooter);
        }

        if (isLast)
        {
            root.Children.Add(HLine(1));
            root.Children.Add(BuildFooter());
        }

        return root;
    }

    private FrameworkElement BuildLinesTable(int startIndex, int count)
    {
        var container = new StackPanel();

        container.Children.Add(BuildTableHeaderRow(Columns, fontSize: TableFontSize));
        container.Children.Add(HLine(0.5));

        for (var i = 0; i < count; i++)
        {
            var lineIndex = startIndex + i;
            var cells = BuildLineCells(lineIndex);
            var isMarker = IsGroupMarkerRow(lineIndex);
            container.Children.Add(BuildTableRow(
                Columns, isHeader: isMarker, isMarker ? Brushes.Transparent : AlternatingRowBackground(i), cells,
                rowHeight: LineHeight, fontSize: TableFontSize));
        }

        return container;
    }
}
