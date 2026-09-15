using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using bmcs_app.Domain.Calculations;

namespace bmcs_app.Reports;

/// <summary>
/// 帳票の A4 <see cref="FixedDocument"/> を組み立てる基底クラス（TODO.md 10-3、帳票基盤）。
/// 参考実装（別リポジトリ <c>bmcs_app.Sales/Services/SalesPrintHelper.cs</c>）のうち、
/// 帳票種別に依存しない部分（A4寸法・改ページ・明細テーブル・共通描画ユーティリティ）だけを
/// テンプレートメソッドとして切り出したもの。派生クラスはヘッダー・フッター・列定義・行データのみを与える。
/// 改ページの分割計算そのものは <see cref="ReportPagination"/>（Domain、単体テスト済み）に委ねる。
/// </summary>
public abstract class ReportDocumentBuilder
{
    // ── A4 寸法（WPF device-independent units: 1/96 インチ。210mm/297mm を正確に変換した値）───
    // Width/Height は ReportPrintService が PrintQueue の PageSize を A4 に固定する際にも使うため public。
    public const double A4Width = 793.7008;
    public const double A4Height = 1122.5197;
    protected const double MarginX = 48.0;
    protected const double MarginY = 44.0;
    protected const double ContentWidth = A4Width - 2 * MarginX;
    protected const double ContentHeight = A4Height - 2 * MarginY;

    protected const double LineHeight = 21.0;
    protected const double TableHeaderHeight = 24.0;

    protected static readonly FontFamily JapaneseFont = new("Meiryo UI");
    private static readonly Brush TableHeaderBackground = new SolidColorBrush(Color.FromRgb(220, 220, 220));
    private static readonly Brush AltRowBackground = new SolidColorBrush(Color.FromRgb(248, 248, 248));
    private static readonly Brush ColumnSeparatorBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));

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

    public FixedDocument Build()
    {
        var linesOnFirstPage = Math.Max(1,
            (int)((ContentHeight - FullHeaderHeight - TableHeaderHeight - FooterHeight) / LineHeight));
        var linesOnLaterPages = Math.Max(1,
            (int)((ContentHeight - CompactHeaderHeight - TableHeaderHeight - FooterHeight) / LineHeight));

        var pageSplits = ReportPagination.Split(LineCount, linesOnFirstPage, linesOnLaterPages);
        var document = new FixedDocument();

        for (var i = 0; i < pageSplits.Count; i++)
        {
            var (startIndex, count) = pageSplits[i];
            AddPage(
                document, startIndex, count,
                isFirst: i == 0, isLast: i == pageSplits.Count - 1,
                pageNumber: i + 1, totalPages: pageSplits.Count);
        }

        return document;
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

        container.Children.Add(BuildTableRow(
            isHeader: true,
            background: TableHeaderBackground,
            cells: Columns.Select(c => (string?)c.Header).ToArray()));
        container.Children.Add(HLine(0.5));

        for (var i = 0; i < count; i++)
        {
            var cells = BuildLineCells(startIndex + i);
            var background = i % 2 == 0 ? Brushes.White : AltRowBackground;
            container.Children.Add(BuildTableRow(isHeader: false, background: background, cells: cells));
        }

        return container;
    }

    protected FrameworkElement BuildTableRow(bool isHeader, Brush background, string?[] cells)
    {
        var widths = ResolveColumnWidths();

        var grid = new Grid
        {
            Background = background,
            Height = isHeader ? TableHeaderHeight : LineHeight,
        };
        foreach (var width in widths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }

        for (var i = 0; i < Columns.Count; i++)
        {
            var text = i < cells.Length ? cells[i] ?? "" : "";
            var textBlock = new TextBlock
            {
                Text = text,
                FontFamily = JapaneseFont,
                FontSize = 9.0,
                FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                TextAlignment = Columns[i].ToTextAlignment(),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(3, 0, 3, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                // MahApps.Metro のテーマ前景色が継承されると印字が薄いグレーになるため明示的に黒にする。
                Foreground = Brushes.Black,
            };
            Grid.SetColumn(textBlock, i);
            grid.Children.Add(textBlock);

            if (i < Columns.Count - 1)
            {
                var separator = new Rectangle
                {
                    Width = 0.5,
                    Fill = ColumnSeparatorBrush,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                Grid.SetColumn(separator, i);
                grid.Children.Add(separator);
            }
        }

        return grid;
    }

    private double[] ResolveColumnWidths()
    {
        var fixedTotal = Columns.Where(c => c.Width > 0).Sum(c => c.Width);
        var starWidth = ContentWidth - fixedTotal;
        return Columns.Select(c => c.Width > 0 ? c.Width : starWidth).ToArray();
    }

    protected static TextBlock Tb(
        string text, double size = 10, FontWeight? weight = null, TextAlignment align = TextAlignment.Left)
        => new()
        {
            Text = text,
            FontFamily = JapaneseFont,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            TextAlignment = align,
            // MahApps.Metro のテーマ前景色が継承されると印字が薄いグレーになるため明示的に黒にする
            // （注記等でグレーにしたい場合は呼び出し元が Foreground を上書きする）。
            Foreground = Brushes.Black,
        };

    protected static Rectangle HLine(double thickness)
        => new()
        {
            Height = thickness,
            Fill = Brushes.Black,
            Margin = new Thickness(0, 2, 0, 2),
        };
}
