using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace bmcs_app.Sales.Services;

/// <summary>売上伝票をインボイス制度準拠の A4 納品書として印刷する</summary>
public static class SalesPrintHelper
{
    // ── A4 寸法（WPF device-independent units: 1/96 インチ）─────────
    private const double A4Width  = 793.92;
    private const double A4Height = 1122.24;
    private const double MX       = 48.0;   // 横マージン
    private const double MY       = 44.0;   // 縦マージン
    private const double CW       = A4Width  - 2 * MX;   // 697.92
    private const double CH       = A4Height - 2 * MY;   // 1034.24

    // ── 行高 ─────────────────────────────────────────────────────
    private const double LineH        = 21.0;
    private const double TableHeaderH = 24.0;

    // ── 各セクション高さ（おおよそ） ──────────────────────────────
    private const double FullHeaderH  = 240.0;  // タイトル+区切り+情報+区切り
    private const double CompactHeaderH = 34.0; // 続紙ヘッダ
    private const double FooterH      = 160.0;  // 税率別集計+合計

    // ── 明細テーブルの列幅 ─────────────────────────────────────────
    // { 行, 商品コード, 商品名(*=残余), 数量, 単価, 金額, 税種, 税率, 行摘要 }
    private static readonly double[] ColFixed = { 28, 82, 0, 52, 72, 76, 46, 42, 76 };
    // index 2 (商品名) = * → 残余幅で計算
    private static double StarWidth => CW - ColFixed.Where(w => w > 0).Sum();  // ≒ 223

    // ── フォント ──────────────────────────────────────────────────
    private static readonly FontFamily JFont = new("Meiryo UI");

    // ─────────────────────────────────────────────────────────────
    //  公開 API
    // ─────────────────────────────────────────────────────────────

    public static void Print(SalePrintData data)
    {
        if (string.IsNullOrWhiteSpace(data.SaleNo))
        {
            MessageBox.Show("印刷する伝票を読み込んでください", "印刷",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;

        var doc = BuildDocument(data);
        dlg.PrintDocument(doc.DocumentPaginator, $"売上伝票 {data.SaleNo}");
    }

    // ─────────────────────────────────────────────────────────────
    //  ドキュメント構築
    // ─────────────────────────────────────────────────────────────

    private static FixedDocument BuildDocument(SalePrintData data)
    {
        var available1 = CH - FullHeaderH  - TableHeaderH - FooterH;
        var availableN = CH - CompactHeaderH - TableHeaderH - FooterH;
        int linesPage1 = Math.Max(1, (int)(available1 / LineH));
        int linesPageN = Math.Max(1, (int)(availableN / LineH));

        var all       = data.Lines;
        var doc       = new FixedDocument();
        var remaining = new List<SalePrintLine>(all);

        var first  = remaining.Take(linesPage1).ToList();
        remaining  = remaining.Skip(linesPage1).ToList();
        int total  = 1 + (remaining.Count > 0
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

        return doc;
    }

    private static void AddFixedPage(FixedDocument doc, SalePrintData data,
        List<SalePrintLine> lines, bool isFirst, bool isLast, int pageNum, int totalPages)
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

    private static StackPanel BuildPageContent(SalePrintData data,
        List<SalePrintLine> lines, bool isFirst, bool isLast, int pageNum, int totalPages)
    {
        var root = new StackPanel { Width = CW, Background = Brushes.White };

        if (isFirst)
        {
            root.Children.Add(BuildTitle(data, totalPages > 1 ? $"（{pageNum}/{totalPages}）" : ""));
            root.Children.Add(HLine(1.5));
            root.Children.Add(BuildInfoRow(data));
            root.Children.Add(BuildSlipMeta(data));
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

    private static FrameworkElement BuildTitle(SalePrintData data, string pageLabel)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 日付（左）
        var dateTb = Tb(data.SaleDate, 9);
        dateTb.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(dateTb, 0);
        grid.Children.Add(dateTb);

        // タイトル（中央）
        var title = Tb("納  品  書", 22, FontWeights.Bold, TextAlignment.Center);
        title.Margin = new Thickness(0, 0, 0, 4);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        // ページ（右）
        if (!string.IsNullOrEmpty(pageLabel))
        {
            var pg = Tb(pageLabel, 9, align: TextAlignment.Right);
            pg.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(pg, 2);
            grid.Children.Add(pg);
        }

        return grid;
    }

    private static FrameworkElement BuildInfoRow(SalePrintData data)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        // 左: 得意先
        var leftSp = new StackPanel();
        leftSp.Children.Add(Tb($"{data.CustomerName}　御中", 16, FontWeights.Bold));

        Grid.SetColumn(leftSp, 0);
        grid.Children.Add(leftSp);

        // 右: 自社情報（枠付き）
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
            var regTb = Tb($"登録番号: {data.CompanyInvoiceRegNo}", 8, FontWeights.Bold, TextAlignment.Right);
            rightSp.Children.Add(regTb);
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

    private static FrameworkElement BuildSlipMeta(SalePrintData data)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Tb("伝票No.:　", 9, FontWeights.Bold));
        row.Children.Add(Tb(data.SaleNo, 9));
        row.Children.Add(new Rectangle { Width = 32, Fill = Brushes.Transparent });
        row.Children.Add(Tb("担当者:　", 9));
        row.Children.Add(Tb(data.EmployeeName, 9));
        sp.Children.Add(row);

        if (!string.IsNullOrWhiteSpace(data.SlipRemarks))
        {
            var r2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            r2.Children.Add(Tb("摘要:　", 9));
            r2.Children.Add(Tb(data.SlipRemarks, 9));
            sp.Children.Add(r2);
        }

        return sp;
    }

    private static FrameworkElement BuildCompactHeader(SalePrintData data, int pageNum, int totalPages)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 4, 0, 4),
        };
        row.Children.Add(Tb("納品書（続き）", 10, FontWeights.Bold));
        row.Children.Add(new Rectangle { Width = 20, Fill = Brushes.Transparent });
        row.Children.Add(Tb($"伝票No. {data.SaleNo}　{data.CustomerName}　御中", 9));
        row.Children.Add(new Rectangle { Width = 1, Fill = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch });
        row.Children.Add(Tb($"{pageNum}/{totalPages} ページ", 8, align: TextAlignment.Right));
        return row;
    }

    // ─────────────────────────────────────────────────────────────
    //  明細テーブル
    // ─────────────────────────────────────────────────────────────

    private static readonly string[] Headers =
        { "行", "商品コード", "商品名", "数量", "単価", "金額", "税種", "税率", "摘要" };

    private static readonly TextAlignment[] ColAlign =
    {
        TextAlignment.Center, TextAlignment.Left, TextAlignment.Left,
        TextAlignment.Right,  TextAlignment.Right, TextAlignment.Right,
        TextAlignment.Center, TextAlignment.Right, TextAlignment.Left,
    };

    private static FrameworkElement BuildLinesTable(List<SalePrintLine> lines)
    {
        var container = new StackPanel();

        // ヘッダー行
        container.Children.Add(BuildTableRow(
            isHeader:   true,
            background: new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            cells: Headers.Select(h => h).ToArray()));

        container.Children.Add(HLine(0.5));

        // データ行
        bool alt = false;
        foreach (var line in lines)
        {
            var cells = new[]
            {
                line.LineNo.ToString(),
                line.ProductCode,
                line.ProductName,
                line.Quantity,
                line.UnitPrice,
                line.LineAmount,
                line.TaxTypeName,
                line.TaxRate,
                line.LineRemarks,
            };
            var bg = alt
                ? new SolidColorBrush(Color.FromRgb(248, 248, 248))
                : Brushes.White;
            container.Children.Add(BuildTableRow(isHeader: false, background: bg, cells: cells));
            alt = !alt;
        }

        // 空行パディング（見た目が詰まらないよう最低高を確保）
        if (lines.Count < 5)
        {
            for (int i = lines.Count; i < 5; i++)
            {
                container.Children.Add(
                    BuildTableRow(isHeader: false, background: Brushes.White,
                        cells: new string[9]));
            }
        }

        return container;
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
            grid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(cols[i]) });

        for (int i = 0; i < cols.Length; i++)
        {
            var text = cells.Length > i ? cells[i] ?? "" : "";
            var align = ColAlign[i];
            var weight = isHeader ? FontWeights.Bold : FontWeights.Normal;
            var fontSize = isHeader ? 9.0 : 9.0;

            var tb = new TextBlock
            {
                Text              = text,
                FontFamily        = JFont,
                FontSize          = fontSize,
                FontWeight        = weight,
                TextAlignment     = align,
                VerticalAlignment = VerticalAlignment.Center,
                Padding           = new Thickness(3, 0, 3, 0),
                TextTrimming      = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(tb, i);
            grid.Children.Add(tb);

            // 列区切り線（右端以外）
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
    //  フッター（税率別集計＋合計）
    // ─────────────────────────────────────────────────────────────

    private static FrameworkElement BuildFooter(SalePrintData data)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // 税率別集計（インボイス制度 必須記載事項）
        if (data.TaxBreakdowns.Count > 0)
        {
            var breakdown = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
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

                breakdown.Children.Add(row);
            }
            sp.Children.Add(breakdown);
            sp.Children.Add(HLine(0.5));
        }

        // 合計ブロック（右寄せ）
        var totalsPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        totalsPanel.Children.Add(BuildTotalRow("税抜合計",  data.TaxExcludedTotalStr,  false));
        totalsPanel.Children.Add(BuildTotalRow("消費税合計", data.TaxTotalStr,          false));
        totalsPanel.Children.Add(HLine(1.5));
        totalsPanel.Children.Add(BuildTotalRow("税込合計",  data.GrandTotalStr,        true));
        sp.Children.Add(totalsPanel);

        // インボイス注記
        var note = Tb("※本書は消費税法に基づく適格請求書（インボイス）です", 7);
        note.Margin     = new Thickness(0, 12, 0, 0);
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

    private static FrameworkElement BuildTotalRow(string label, string value, bool large)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        var labelTb = Tb(label, large ? 10.0 : 9.0,
            large ? FontWeights.Bold : FontWeights.Normal);
        labelTb.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(labelTb, 0);
        grid.Children.Add(labelTb);

        var valueTb = Tb(value, large ? 14.0 : 10.0,
            large ? FontWeights.Bold : FontWeights.Normal, TextAlignment.Right);
        valueTb.VerticalAlignment = VerticalAlignment.Center;
        if (large)
            valueTb.Foreground = Brushes.Black;
        Grid.SetColumn(valueTb, 1);
        grid.Children.Add(valueTb);

        return grid;
    }

    // ─────────────────────────────────────────────────────────────
    //  ユーティリティ
    // ─────────────────────────────────────────────────────────────

    private static TextBlock Tb(
        string text,
        double size   = 10,
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
