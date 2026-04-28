using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Printing;
using bmcs_app.Core.Models;

namespace bmcs_app.Closing.Services;

public static class ArBalancePrintHelper
{
    // ── A4 寸法 ───────────────────────────────────────────────────
    private const double A4Width  = 793.92;
    private const double A4Height = 1122.24;
    private const double MX       = 40.0;
    private const double MY       = 40.0;
    private const double CW       = A4Width  - 2 * MX;
    private const double CH       = A4Height - 2 * MY;

    // ── 行高 ─────────────────────────────────────────────────────
    private const double LineH        = 20.0;
    private const double TableHeaderH = 24.0;

    // ── セクション高さ ─────────────────────────────────────────────
    private const double FullHeaderH    = 56.0;
    private const double CompactHeaderH = 28.0;
    private const double TotalRowH      = 24.0;

    // ── 列定義 ────────────────────────────────────────────────────
    // { コード, 得意先名(*), 前回残高, 当期売上, 消費税, 入金, 今回残高 }
    private static readonly double[] ColFixed = { 80, 0, 90, 90, 80, 90, 90 };
    private static double StarWidth => CW - ColFixed.Where(w => w > 0).Sum();

    private static readonly string[] ColHeaders =
        { "得意先コード", "得意先名", "前回残高", "当期売上", "消費税", "入金", "今回残高" };

    private static readonly TextAlignment[] ColAlign =
    {
        TextAlignment.Left, TextAlignment.Left,
        TextAlignment.Right, TextAlignment.Right, TextAlignment.Right,
        TextAlignment.Right, TextAlignment.Right,
    };

    // ── フォント ──────────────────────────────────────────────────
    private static readonly FontFamily JFont = new("Meiryo UI");

    // ─────────────────────────────────────────────────────────────
    //  公開 API
    // ─────────────────────────────────────────────────────────────

    public static void Print(IEnumerable<ArBalanceRow> rows, DateOnly closingDate, string companyName)
    {
        var list = rows.ToList();
        if (list.Count == 0)
        {
            MessageBox.Show("印刷する売掛金データがありません", "印刷",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var doc      = BuildDocument(list, closingDate, companyName);
        var fallback = new PrintDialog();
        if (fallback.ShowDialog() != true) return;
        fallback.PrintDocument(doc.DocumentPaginator, "売掛金残高一覧表");
    }

    // ─────────────────────────────────────────────────────────────
    //  ドキュメント構築
    // ─────────────────────────────────────────────────────────────

    private static FixedDocument BuildDocument(
        List<ArBalanceRow> rows, DateOnly closingDate, string companyName)
    {
        var available1 = CH - FullHeaderH    - TableHeaderH - TotalRowH;
        var availableN = CH - CompactHeaderH - TableHeaderH - TotalRowH;
        int linesPage1 = Math.Max(1, (int)(available1 / LineH));
        int linesPageN = Math.Max(1, (int)(availableN / LineH));

        var remaining = rows.ToList();
        var first     = remaining.Take(linesPage1).ToList();
        remaining     = remaining.Skip(linesPage1).ToList();
        int total     = 1 + (remaining.Count > 0
            ? (int)Math.Ceiling(remaining.Count / (double)linesPageN)
            : 0);

        var doc = new FixedDocument();
        AddPage(doc, rows, first, closingDate, companyName,
                isFirst: true, isLast: remaining.Count == 0,
                pageNum: 1,    totalPages: total);

        int pg = 2;
        while (remaining.Count > 0)
        {
            var chunk = remaining.Take(linesPageN).ToList();
            remaining = remaining.Skip(linesPageN).ToList();
            AddPage(doc, rows, chunk, closingDate, companyName,
                    isFirst: false, isLast: remaining.Count == 0,
                    pageNum: pg++,  totalPages: total);
        }

        return doc;
    }

    private static void AddPage(FixedDocument doc, List<ArBalanceRow> allRows,
        List<ArBalanceRow> pageRows, DateOnly closingDate, string companyName,
        bool isFirst, bool isLast, int pageNum, int totalPages)
    {
        var fp = new FixedPage
        {
            Width      = A4Width,
            Height     = A4Height,
            Background = Brushes.White,
        };

        var content = BuildPageContent(allRows, pageRows, closingDate, companyName,
                                       isFirst, isLast, pageNum, totalPages);
        FixedPage.SetLeft(content, MX);
        FixedPage.SetTop(content,  MY);
        fp.Children.Add(content);

        fp.Measure(new Size(A4Width, A4Height));
        fp.Arrange(new Rect(0, 0, A4Width, A4Height));
        fp.UpdateLayout();

        var pc = new PageContent();
        ((IAddChild)pc).AddChild(fp);
        doc.Pages.Add(pc);
    }

    // ─────────────────────────────────────────────────────────────
    //  ページコンテンツ
    // ─────────────────────────────────────────────────────────────

    private static StackPanel BuildPageContent(
        List<ArBalanceRow> allRows, List<ArBalanceRow> pageRows,
        DateOnly closingDate, string companyName,
        bool isFirst, bool isLast, int pageNum, int totalPages)
    {
        var root = new StackPanel { Width = CW, Background = Brushes.White };

        if (isFirst)
            root.Children.Add(BuildFullHeader(closingDate, companyName, pageNum, totalPages));
        else
            root.Children.Add(BuildCompactHeader(closingDate, pageNum, totalPages));

        root.Children.Add(HLine(1));
        root.Children.Add(BuildTableHeader());
        root.Children.Add(HLine(0.5));

        foreach (var row in pageRows)
            root.Children.Add(BuildDataRow(row));

        if (isLast)
        {
            root.Children.Add(HLine(1));
            root.Children.Add(BuildTotalRow(allRows));
        }

        return root;
    }

    // ─────────────────────────────────────────────────────────────
    //  ヘッダー
    // ─────────────────────────────────────────────────────────────

    private static FrameworkElement BuildFullHeader(
        DateOnly closingDate, string companyName, int pageNum, int totalPages)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var title = Tb("売掛金残高一覧表", 16, FontWeights.Bold, TextAlignment.Center);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(Tb($"集計日付: {closingDate:yyyy/MM/dd}", 9, align: TextAlignment.Right));
        right.Children.Add(Tb(companyName, 9, align: TextAlignment.Right));
        if (totalPages > 1)
            right.Children.Add(Tb($"{pageNum} / {totalPages} ページ", 8, align: TextAlignment.Right));
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return grid;
    }

    private static FrameworkElement BuildCompactHeader(DateOnly closingDate, int pageNum, int totalPages)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 4, 0, 4),
        };
        sp.Children.Add(Tb("売掛金残高一覧表（続き）", 10, FontWeights.Bold));
        sp.Children.Add(new Rectangle { Width = 16, Fill = Brushes.Transparent });
        sp.Children.Add(Tb($"集計日付: {closingDate:yyyy/MM/dd}", 9));
        sp.Children.Add(new Rectangle { Width = 1, Fill = Brushes.Transparent,
                                        HorizontalAlignment = HorizontalAlignment.Stretch });
        sp.Children.Add(Tb($"{pageNum}/{totalPages} ページ", 8, align: TextAlignment.Right));
        return sp;
    }

    // ─────────────────────────────────────────────────────────────
    //  テーブル
    // ─────────────────────────────────────────────────────────────

    private static FrameworkElement BuildTableHeader()
        => BuildRow(ColHeaders, isHeader: true, background: new SolidColorBrush(Color.FromRgb(220, 220, 220)));

    private static FrameworkElement BuildDataRow(ArBalanceRow row)
    {
        var cells = new[]
        {
            row.CustomerCode,
            row.CustomerName,
            Fmt(row.CarriedOverAmount),
            Fmt(row.SalesAmountStandard + row.SalesAmountReduced),
            Fmt(row.TaxAmountStandard   + row.TaxAmountReduced),
            Fmt(row.ReceiptAmount),
            Fmt(row.ClosingAmount),
        };
        return BuildRow(cells, isHeader: false, background: Brushes.White);
    }

    private static FrameworkElement BuildTotalRow(List<ArBalanceRow> rows)
    {
        var cells = new[]
        {
            "合  計",
            "",
            Fmt(rows.Sum(r => r.CarriedOverAmount)),
            Fmt(rows.Sum(r => r.SalesAmountStandard + r.SalesAmountReduced)),
            Fmt(rows.Sum(r => r.TaxAmountStandard   + r.TaxAmountReduced)),
            Fmt(rows.Sum(r => r.ReceiptAmount)),
            Fmt(rows.Sum(r => r.ClosingAmount)),
        };
        return BuildRow(cells, isHeader: false,
                        background: new SolidColorBrush(Color.FromRgb(235, 235, 235)),
                        isBold: true, rowHeight: TotalRowH);
    }

    private static FrameworkElement BuildRow(
        string[] cells, bool isHeader, Brush background,
        bool isBold = false, double? rowHeight = null)
    {
        var starW = StarWidth;
        var cols  = ColFixed.Select(w => w == 0 ? starW : w).ToArray();
        var h     = rowHeight ?? (isHeader ? TableHeaderH : LineH);

        var grid = new Grid { Background = background, Height = h };
        for (int i = 0; i < cols.Length; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cols[i]) });

        for (int i = 0; i < cols.Length; i++)
        {
            var tb = new TextBlock
            {
                Text              = cells[i],
                FontFamily        = JFont,
                FontSize          = 9.0,
                FontWeight        = (isHeader || isBold) ? FontWeights.Bold : FontWeights.Normal,
                TextAlignment     = ColAlign[i],
                VerticalAlignment = VerticalAlignment.Center,
                Padding           = new Thickness(3, 0, 3, 0),
                TextTrimming      = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(tb, i);
            grid.Children.Add(tb);

            if (i < cols.Length - 1)
            {
                var sep = new Rectangle
                {
                    Width               = 0.5,
                    Fill                = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment   = VerticalAlignment.Stretch,
                };
                Grid.SetColumn(sep, i);
                grid.Children.Add(sep);
            }
        }

        return grid;
    }

    // ─────────────────────────────────────────────────────────────
    //  ユーティリティ
    // ─────────────────────────────────────────────────────────────

    private static string Fmt(decimal v) => v.ToString("#,##0");

    private static TextBlock Tb(
        string text, double size = 10,
        FontWeight? weight = null, TextAlignment align = TextAlignment.Left)
        => new()
        {
            Text          = text,
            FontFamily    = JFont,
            FontSize      = size,
            FontWeight    = weight ?? FontWeights.Normal,
            TextAlignment = align,
        };

    private static Rectangle HLine(double thickness)
        => new()
        {
            Height = thickness,
            Fill   = Brushes.Black,
            Margin = new Thickness(0, 2, 0, 2),
        };
}
