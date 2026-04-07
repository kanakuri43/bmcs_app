using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using bmcs_app.Infrastructure;

namespace bmcs_app.Closing.Services;

public static class InvoicePrintHelper
{
    // ── A4 寸法（WPF device-independent units: 1/96 インチ）─────────
    private const double A4Width  = 793.92;
    private const double A4Height = 1122.24;
    private const double MX       = 48.0;
    private const double MY       = 44.0;
    private const double CW       = A4Width  - 2 * MX;   // 697.92
    private const double CH       = A4Height - 2 * MY;   // 1034.24

    // ── 行高 ─────────────────────────────────────────────────────
    private const double LineH        = 20.0;
    private const double TableHeaderH = 24.0;

    // ── 各セクション高さ ──────────────────────────────────────────
    private const double FullHeaderH    = 196.0;  // タイトル+区切り+情報行+メタ+区切り
    private const double SummaryH       = 178.0;  // 請求額集計ブロック
    private const double CompactHeaderH = 34.0;
    private const double FooterH        = 80.0;   // 税率別集計+注記

    // ── 明細テーブルの列幅 ─────────────────────────────────────────
    // { 日付, 伝票No., 摘要(*=残余), 税抜金額/入金額, 消費税 }
    private static readonly double[] ColFixed = { 72, 96, 0, 90, 76 };
    private static double StarWidth => CW - ColFixed.Where(w => w > 0).Sum();  // ≒ 363

    // ── 印刷行（売上行・入金行・セクション区切りを統一）─────────────
    private record PrintRow(string C0, string C1, string C2, string C3, string C4, bool IsSection = false);

    // ── フォント ──────────────────────────────────────────────────
    private static readonly FontFamily JFont = new("Meiryo UI");

    // ─────────────────────────────────────────────────────────────
    //  公開 API
    // ─────────────────────────────────────────────────────────────

    public static void Print(IEnumerable<InvoicePrintData> invoices)
    {
        var list = invoices.ToList();
        if (list.Count == 0)
        {
            MessageBox.Show("印刷する請求データがありません", "印刷",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var doc         = BuildDocument(list);
        var settings    = PrinterSettingsConfig.Load();
        var printerName = settings.InvoicePrinter;

        if (!string.IsNullOrWhiteSpace(printerName))
        {
            try
            {
                var dlg = new PrintDialog
                {
                    PrintQueue = new PrintQueue(new LocalPrintServer(), printerName),
                };
                dlg.PrintDocument(doc.DocumentPaginator, "請求書");
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"設定されたプリンタへの送信に失敗しました。\n{ex.Message}\n\n印刷ダイアログで再試行します。",
                    "印刷エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        var fallback = new PrintDialog();
        if (fallback.ShowDialog() != true) return;
        fallback.PrintDocument(doc.DocumentPaginator, "請求書");
    }

    // ─────────────────────────────────────────────────────────────
    //  ドキュメント構築（得意先ごとにページ追加）
    // ─────────────────────────────────────────────────────────────

    private static FixedDocument BuildDocument(List<InvoicePrintData> invoices)
    {
        var doc = new FixedDocument();
        foreach (var data in invoices)
            AddInvoicePages(doc, data);
        return doc;
    }

    private static List<PrintRow> BuildPrintRows(InvoicePrintData data)
    {
        var rows = new List<PrintRow>();

        foreach (var l in data.Lines)
            rows.Add(new PrintRow(l.SaleDate, l.SaleNo, l.Remarks, l.TaxExcluded, l.TaxAmount));

        if (data.ReceiptLines.Count > 0)
        {
            rows.Add(new PrintRow("", "-- 入金 --", "", "", "", IsSection: true));
            foreach (var r in data.ReceiptLines)
                rows.Add(new PrintRow(r.ReceiptDate, r.ReceiptNo, r.Remarks, r.AmountStr, ""));
        }

        return rows;
    }

    private static void AddInvoicePages(FixedDocument doc, InvoicePrintData data)
    {
        var available1 = CH - FullHeaderH - SummaryH - TableHeaderH - FooterH;
        var availableN = CH - CompactHeaderH - TableHeaderH - FooterH;
        int linesPage1 = Math.Max(1, (int)(available1 / LineH));
        int linesPageN = Math.Max(1, (int)(availableN / LineH));

        var remaining = BuildPrintRows(data);

        var first    = remaining.Take(linesPage1).ToList();
        remaining    = remaining.Skip(linesPage1).ToList();
        int total    = 1 + (remaining.Count > 0
            ? (int)Math.Ceiling(remaining.Count / (double)linesPageN)
            : 0);

        AddFixedPage(doc, data, first,
            isFirst: true, isLast: remaining.Count == 0,
            pageNum: 1,    totalPages: total);

        int pg = 2;
        while (remaining.Count > 0)
        {
            var chunk  = remaining.Take(linesPageN).ToList();
            remaining  = remaining.Skip(linesPageN).ToList();
            AddFixedPage(doc, data, chunk,
                isFirst: false, isLast: remaining.Count == 0,
                pageNum: pg++,  totalPages: total);
        }
    }

    private static void AddFixedPage(FixedDocument doc, InvoicePrintData data,
        List<PrintRow> lines, bool isFirst, bool isLast, int pageNum, int totalPages)
    {
        var fp = new FixedPage
        {
            Width      = A4Width,
            Height     = A4Height,
            Background = Brushes.White,
        };

        var content = BuildPageContent(data, lines, isFirst, isLast, pageNum, totalPages);
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

    private static StackPanel BuildPageContent(InvoicePrintData data,
        List<PrintRow> lines, bool isFirst, bool isLast, int pageNum, int totalPages)
    {
        var root = new StackPanel { Width = CW, Background = Brushes.White };

        if (isFirst)
        {
            root.Children.Add(BuildTitle(data, totalPages > 1 ? $"（{pageNum}/{totalPages}）" : ""));
            root.Children.Add(HLine(1.5));
            root.Children.Add(BuildInfoRow(data));
            root.Children.Add(BuildMeta(data));
            root.Children.Add(HLine(1));
            root.Children.Add(BuildSummary(data));
            root.Children.Add(HLine(1));
        }
        else
        {
            root.Children.Add(BuildCompactHeader(data, pageNum, totalPages));
            root.Children.Add(HLine(1));
        }

        root.Children.Add(BuildLinesTable(lines));

        if (isLast)
        {
            root.Children.Add(HLine(1));
            root.Children.Add(BuildFooter(data));
        }

        return root;
    }

    // ─────────────────────────────────────────────────────────────
    //  ヘッダーセクション
    // ─────────────────────────────────────────────────────────────

    private static FrameworkElement BuildTitle(InvoicePrintData data, string pageLabel)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dateTb = Tb(data.InvoiceDate, 9);
        dateTb.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(dateTb, 0);
        grid.Children.Add(dateTb);

        var title = Tb("請  求  書", 22, FontWeights.Bold, TextAlignment.Center);
        title.Margin = new Thickness(0, 0, 0, 4);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        if (!string.IsNullOrEmpty(pageLabel))
        {
            var pg = Tb(pageLabel, 9, align: TextAlignment.Right);
            pg.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(pg, 2);
            grid.Children.Add(pg);
        }

        return grid;
    }

    private static FrameworkElement BuildInfoRow(InvoicePrintData data)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        var leftSp = new StackPanel();
        if (!string.IsNullOrWhiteSpace(data.CustomerPostalCode))
            leftSp.Children.Add(Tb($"〒 {data.CustomerPostalCode}", 9));
        if (!string.IsNullOrWhiteSpace(data.CustomerAddress1))
            leftSp.Children.Add(Tb(data.CustomerAddress1, 9));
        if (!string.IsNullOrWhiteSpace(data.CustomerAddress2))
            leftSp.Children.Add(Tb(data.CustomerAddress2, 9));
        leftSp.Children.Add(Tb($"{data.CustomerName}　御中", 16, FontWeights.Bold));
        Grid.SetColumn(leftSp, 0);
        grid.Children.Add(leftSp);

        var rightSp = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        rightSp.Children.Add(Tb(data.CompanyName, 11, FontWeights.Bold, TextAlignment.Right));
        if (!string.IsNullOrWhiteSpace(data.CompanyAddress))
            rightSp.Children.Add(Tb(data.CompanyAddress, 8, align: TextAlignment.Right));
        if (!string.IsNullOrWhiteSpace(data.CompanyPhone))
            rightSp.Children.Add(Tb($"TEL: {data.CompanyPhone}", 8, align: TextAlignment.Right));
        if (!string.IsNullOrWhiteSpace(data.CompanyFax))
            rightSp.Children.Add(Tb($"FAX: {data.CompanyFax}", 8, align: TextAlignment.Right));
        if (!string.IsNullOrWhiteSpace(data.CompanyInvoiceRegNo))
        {
            rightSp.Children.Add(new Rectangle { Height = 4, Fill = Brushes.Transparent });
            rightSp.Children.Add(Tb($"登録番号: {data.CompanyInvoiceRegNo}", 8, FontWeights.Bold, TextAlignment.Right));
        }

        var box = new Border
        {
            BorderBrush     = Brushes.Black,
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(6, 4, 6, 4),
            Child           = rightSp,
        };
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);

        return grid;
    }

    private static FrameworkElement BuildMeta(InvoicePrintData data)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 2, 0, 6),
        };
        row.Children.Add(Tb("請求日:", 9, FontWeights.Bold));
        row.Children.Add(new Rectangle { Width = 4, Fill = Brushes.Transparent });
        row.Children.Add(Tb(data.InvoiceDate, 9));
        row.Children.Add(new Rectangle { Width = 24, Fill = Brushes.Transparent });
        row.Children.Add(Tb("締め日:", 9, FontWeights.Bold));
        row.Children.Add(new Rectangle { Width = 4, Fill = Brushes.Transparent });
        row.Children.Add(Tb(data.ClosingDayLabel, 9));
        return row;
    }

    private static FrameworkElement BuildCompactHeader(InvoicePrintData data, int pageNum, int totalPages)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 4, 0, 4),
        };
        row.Children.Add(Tb("請求書（続き）", 10, FontWeights.Bold));
        row.Children.Add(new Rectangle { Width = 20, Fill = Brushes.Transparent });
        row.Children.Add(Tb($"{data.CustomerName}　御中　{data.InvoiceDate}", 9));
        row.Children.Add(new Rectangle { Width = 1, Fill = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch });
        row.Children.Add(Tb($"{pageNum}/{totalPages} ページ", 8, align: TextAlignment.Right));
        return row;
    }

    // ─────────────────────────────────────────────────────────────
    //  集計サマリーセクション（1ページ目のみ）
    // ─────────────────────────────────────────────────────────────

    private static FrameworkElement BuildSummary(InvoicePrintData data)
    {
        var outer = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });

        // 右: 今回請求額ボックス
        var amountBox = BuildCurrentAmountBox(data.CurrentAmountStr);
        Grid.SetColumn(amountBox, 1);
        outer.Children.Add(amountBox);

        // 左: 内訳
        var leftSp = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };

        leftSp.Children.Add(BuildSummaryRow("前回請求額",            data.PreviousAmountStr,  false));
        leftSp.Children.Add(BuildSummaryRow("入金額",               data.ReceiptAmountStr,   false, prefix: "－"));
        leftSp.Children.Add(HLine(0.5));

        bool hasStandard = data.SalesStandardStr != "0" || data.TaxStandardStr != "0";
        bool hasReduced  = data.SalesReducedStr  != "0" || data.TaxReducedStr  != "0";

        if (hasStandard)
        {
            leftSp.Children.Add(BuildSummaryRow("今期売上（税抜・標準）", data.SalesStandardStr, false));
            leftSp.Children.Add(BuildSummaryRow("消費税（標準）",        data.TaxStandardStr,   false));
        }
        if (hasReduced)
        {
            leftSp.Children.Add(BuildSummaryRow("今期売上（税抜・軽減）", data.SalesReducedStr, false));
            leftSp.Children.Add(BuildSummaryRow("消費税（軽減）",        data.TaxReducedStr,   false));
        }

        leftSp.Children.Add(HLine(1));
        leftSp.Children.Add(BuildSummaryRow("今回請求額", data.CurrentAmountStr, true));

        Grid.SetColumn(leftSp, 0);
        outer.Children.Add(leftSp);

        return outer;
    }

    private static FrameworkElement BuildCurrentAmountBox(string amount)
    {
        var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        inner.Children.Add(Tb("今回請求額", 9, align: TextAlignment.Center));
        inner.Children.Add(new Rectangle { Height = 6, Fill = Brushes.Transparent });
        inner.Children.Add(Tb($"¥ {amount}", 18, FontWeights.Bold, TextAlignment.Center));

        return new Border
        {
            BorderBrush     = Brushes.Black,
            BorderThickness = new Thickness(1.5),
            Padding         = new Thickness(12, 8, 12, 8),
            Margin          = new Thickness(0, 0, 0, 0),
            Child           = inner,
            VerticalAlignment = VerticalAlignment.Top,
        };
    }

    private static FrameworkElement BuildSummaryRow(string label, string value, bool large, string prefix = "")
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        var prefixTb = Tb(prefix, 9);
        prefixTb.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(prefixTb, 0);
        grid.Children.Add(prefixTb);

        var labelTb = Tb(label, large ? 10.0 : 9.0, large ? FontWeights.Bold : FontWeights.Normal);
        labelTb.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(labelTb, 1);
        grid.Children.Add(labelTb);

        var valueTb = Tb(value, large ? 12.0 : 9.0,
            large ? FontWeights.Bold : FontWeights.Normal, TextAlignment.Right);
        valueTb.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(valueTb, 2);
        grid.Children.Add(valueTb);

        return grid;
    }

    // ─────────────────────────────────────────────────────────────
    //  明細テーブル
    // ─────────────────────────────────────────────────────────────

    private static readonly string[] Headers = { "日付", "伝票No.", "摘要", "税抜/入金額", "消費税" };

    private static readonly TextAlignment[] ColAlign =
    {
        TextAlignment.Center, TextAlignment.Left, TextAlignment.Left,
        TextAlignment.Right,  TextAlignment.Right,
    };

    private static FrameworkElement BuildLinesTable(List<PrintRow> rows)
    {
        var container = new StackPanel();

        container.Children.Add(BuildTableRow(
            isHeader:   true,
            background: new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            cells:      Headers));

        container.Children.Add(HLine(0.5));

        bool alt = false;
        foreach (var row in rows)
        {
            if (row.IsSection)
            {
                container.Children.Add(BuildSectionRow(row.C1));
                alt = false;
                continue;
            }
            var cells = new[] { row.C0, row.C1, row.C2, row.C3, row.C4 };
            var bg    = alt
                ? new SolidColorBrush(Color.FromRgb(248, 248, 248))
                : Brushes.White;
            container.Children.Add(BuildTableRow(isHeader: false, background: bg, cells: cells));
            alt = !alt;
        }

        int nonSectionCount = rows.Count(r => !r.IsSection);
        if (nonSectionCount < 5)
        {
            for (int i = nonSectionCount; i < 5; i++)
                container.Children.Add(
                    BuildTableRow(isHeader: false, background: Brushes.White,
                        cells: new string[Headers.Length]));
        }

        return container;
    }

    private static FrameworkElement BuildSectionRow(string label)
    {
        var grid = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(230, 230, 240)),
            Height     = LineH,
        };
        var tb = new TextBlock
        {
            Text              = label,
            FontFamily        = JFont,
            FontSize          = 9.0,
            FontWeight        = FontWeights.Bold,
            TextAlignment     = TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Padding           = new Thickness(6, 0, 3, 0),
        };
        grid.Children.Add(tb);
        return grid;
    }

    private static FrameworkElement BuildTableRow(bool isHeader, Brush background, string?[] cells)
    {
        var starW = StarWidth;
        var cols  = ColFixed.Select((w, i) => w == 0 ? starW : w).ToArray();

        var grid = new Grid
        {
            Background = background,
            Height     = isHeader ? TableHeaderH : LineH,
        };
        for (int i = 0; i < cols.Length; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cols[i]) });

        for (int i = 0; i < cols.Length; i++)
        {
            var text   = cells.Length > i ? cells[i] ?? "" : "";
            var tb     = new TextBlock
            {
                Text              = text,
                FontFamily        = JFont,
                FontSize          = 9.0,
                FontWeight        = isHeader ? FontWeights.Bold : FontWeights.Normal,
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
    //  フッター（税率別集計＋注記）
    // ─────────────────────────────────────────────────────────────

    private static FrameworkElement BuildFooter(InvoicePrintData data)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        if (data.TaxBreakdowns.Count > 0)
        {
            foreach (var bd in data.TaxBreakdowns)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

                var label = Tb($"※ {bd.Label}", 9);
                Grid.SetColumn(label, 0);
                row.Children.Add(label);

                var excl = BuildLabelValue("税抜金額", bd.TaxExcludedAmount);
                Grid.SetColumn(excl, 1);
                row.Children.Add(excl);

                var tax = BuildLabelValue("消費税", bd.TaxAmount);
                Grid.SetColumn(tax, 3);
                row.Children.Add(tax);

                sp.Children.Add(row);
            }
            sp.Children.Add(HLine(0.5));
        }

        var note = Tb("※本書は消費税法に基づく適格請求書（インボイス）です", 7);
        note.Margin     = new Thickness(0, 6, 0, 0);
        note.Foreground = Brushes.Gray;
        sp.Children.Add(note);

        return sp;
    }

    private static FrameworkElement BuildLabelValue(string label, string value)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(Tb($"{label}:", 9));
        sp.Children.Add(new Rectangle { Width = 4, Fill = Brushes.Transparent });
        var valTb = Tb(value, 9, FontWeights.Bold, TextAlignment.Right);
        valTb.MinWidth = 80;
        sp.Children.Add(valTb);
        return sp;
    }

    // ─────────────────────────────────────────────────────────────
    //  ユーティリティ
    // ─────────────────────────────────────────────────────────────

    private static TextBlock Tb(
        string text,
        double size        = 10,
        FontWeight? weight = null,
        TextAlignment align = TextAlignment.Left)
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
