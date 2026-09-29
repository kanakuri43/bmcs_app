using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using bmcs_app.Domain.Calculations;

namespace bmcs_app.Reports;

/// <summary>
/// 改ページを伴う単一フロー帳票（請求書・明細請求書）の A4 <see cref="FixedDocument"/> を
/// 組み立てる基底クラス（TODO.md 10-3、帳票基盤。2026-09-17、納品書3段複写対応で
/// <see cref="ReportDocumentBuilder"/> から分割）。派生クラスはヘッダー・フッター・列定義・
/// 行データのみを与える。改ページの分割計算そのものは <see cref="ReportPagination"/>
/// （Domain、単体テスト済み）に委ねる。
/// </summary>
public abstract class PagedReportDocumentBuilder : ReportDocumentBuilder
{
    /// <summary>1ページ目のヘッダー（タイトル・得意先・自社情報等）の高さの見積り。</summary>
    protected abstract double FullHeaderHeight { get; }

    /// <summary>2ページ目以降の続紙ヘッダーの高さの見積り。</summary>
    protected abstract double CompactHeaderHeight { get; }

    /// <summary>最終ページのフッター（税率別内訳・合計等）の高さの見積り。</summary>
    protected abstract double FooterHeight { get; }

    /// <summary>明細テーブルの列定義。幅0の列は残余幅になる（最大1列まで）。</summary>
    protected abstract IReadOnlyList<ReportColumn> Columns { get; }

    /// <summary>明細行の総数。</summary>
    protected abstract int LineCount { get; }

    /// <summary>1ページ目の先頭に出すフルヘッダー。</summary>
    protected abstract FrameworkElement BuildFullHeader();

    /// <summary>2ページ目以降の先頭に出す続紙ヘッダー。</summary>
    protected abstract FrameworkElement BuildCompactHeader(int pageNumber, int totalPages);

    /// <summary>最終ページの明細テーブルの下に出すフッター。</summary>
    protected abstract FrameworkElement BuildFooter();

    /// <summary><paramref name="lineIndex"/>（0始まり）の行を <see cref="Columns"/> と同じ順序のセル文字列で返す。</summary>
    protected abstract string?[] BuildLineCells(int lineIndex);

    /// <summary>
    /// 見出し行・小計行のような、通常の明細行とは異なる太字・単色背景で描画すべき行かどうか
    /// （TODO.md 12-D、親子請求）。既定はfalse（全行が通常の明細行）。
    /// </summary>
    protected virtual bool IsGroupMarkerRow(int lineIndex) => false;

    /// <summary>
    /// ページ末尾に来ると孤立してしまう行（見出し行）かどうか（TODO.md 12-D、親子請求）。
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
    /// 追加する（TODO.md 10-7、複数請求書を1つの<see cref="FixedDocument"/>にまとめて印刷する用途）。
    /// ページ番号（<c>pageNumber</c>/<c>totalPages</c>）はこのビルダー分だけで1始まりに閉じる
    /// （請求書ごとに「1/N ページ」と表示するため、連結先の既存ページ数はここでは加味しない）。
    /// </summary>
    public void BuildInto(FixedDocument document)
    {
        var linesOnFirstPage = Math.Max(1,
            (int)((ContentHeight - FullHeaderHeight - TableHeaderHeight - FooterHeight) / LineHeight));
        var linesOnLaterPages = Math.Max(1,
            (int)((ContentHeight - CompactHeaderHeight - TableHeaderHeight - FooterHeight) / LineHeight));

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
            Width = A4Width,
            Height = A4Height,
            Background = Brushes.White,
        };

        var content = BuildPageContent(startIndex, count, isFirst, isLast, pageNumber, totalPages);
        FixedPage.SetLeft(content, MarginX);
        FixedPage.SetTop(content, MarginY);
        fixedPage.Children.Add(content);

        // FixedPage は Show/ShowDialog のビジュアルツリーに乗らないため、
        // レイアウトを自分で確定させる必要がある（参考実装と同じ手順）。
        fixedPage.Measure(new Size(A4Width, A4Height));
        fixedPage.Arrange(new Rect(0, 0, A4Width, A4Height));
        fixedPage.UpdateLayout();

        var pageContent = new PageContent();
        ((IAddChild)pageContent).AddChild(fixedPage);
        document.Pages.Add(pageContent);
    }

    private FrameworkElement BuildPageContent(
        int startIndex, int count, bool isFirst, bool isLast, int pageNumber, int totalPages)
    {
        var root = new StackPanel { Width = ContentWidth, Background = Brushes.White };

        root.Children.Add(isFirst ? BuildFullHeader() : BuildCompactHeader(pageNumber, totalPages));
        root.Children.Add(HLine(1));
        root.Children.Add(BuildLinesTable(startIndex, count));

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

        container.Children.Add(BuildTableHeaderRow(Columns));
        container.Children.Add(HLine(0.5));

        for (var i = 0; i < count; i++)
        {
            var lineIndex = startIndex + i;
            var cells = BuildLineCells(lineIndex);
            var isMarker = IsGroupMarkerRow(lineIndex);
            container.Children.Add(BuildTableRow(
                Columns, isHeader: isMarker, isMarker ? Brushes.Transparent : AlternatingRowBackground(i), cells,
                rowHeight: LineHeight));
        }

        return container;
    }
}
