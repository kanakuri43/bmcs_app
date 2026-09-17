using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using bmcs_app.Application.Sales;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Reports;

/// <summary>
/// 納品書のレイアウト（TODO.md 10-4、2026-09-17改訂：ミシン目入りA4用紙・3段複写に変更）。
/// 参考実装（別リポジトリ <c>bmcs_app.Sales/Services/SalesPrintHelper.cs</c>の
/// <c>BuildTripleDocument</c>系）を移植したもの。1枚のA4を<see cref="SectionTitles"/>の3セクションに
/// 分け、同一内容を「納品書（控）」「請求書」「納品書」の順に印字する（内容は3セクションとも同一で、
/// タイトルのみ異なる。複写紙の代わりに手で切り分けて配布する運用）。
/// 適格請求書としては扱わない（design_document.md の【要確認】が未解決のため注記を出さない）。
/// 担当者は <c>sales</c> に列が無いため印字しない。
/// </summary>
public sealed class DeliveryNoteDocumentBuilder(DeliveryNoteData data) : ReportDocumentBuilder
{
    private static readonly IReadOnlyList<ReportColumn> ColumnDefinitions =
    [
        new("行", 28, ReportColumnAlign.Center),
        new("商品コード", 82, ReportColumnAlign.Left),
        new("商品名", 0, ReportColumnAlign.Left),
        new("数量", 52, ReportColumnAlign.Right),
        new("単価", 72, ReportColumnAlign.Right),
        new("金額", 76, ReportColumnAlign.Right),
        new("税率", 42, ReportColumnAlign.Right),
        new("摘要", 76, ReportColumnAlign.Left),
    ];

    private readonly IReadOnlyList<ReportColumn> _columns = data.TaxUnit == TaxUnit.Line
        ? ColumnDefinitions.Select((c, i) => i switch
            {
                4 => c with { Header = "単価(税込)" },
                5 => c with { Header = "金額(税込)" },
                _ => c,
            }).ToList()
        : ColumnDefinitions;

    // ── 3段複写のセクション定義。ミシン目で3等分された用紙に、上から順にこのタイトルで印字する ──
    private static readonly string[] SectionTitles = ["納品書（控）", "請求書", "納品書"];

    private const double SectionGap = 12.0;
    private const double SectionHeight = (ContentHeight - 2 * SectionGap) / 3;

    // ── 1セクションに3つ分詰めるため、通常の単一フロー帳票より縮小した寸法を使う ──────────
    private const double CondensedTableHeaderHeight = 18.0;
    private const double CondensedLineHeight = 16.0;
    private const double CondensedFontSize = 9.5;

    /// <summary>1セクションあたりの明細行数（固定。ミシン目位置を合わせるため空行にも罫線を引く）。</summary>
    private const int LinesPerSection = 6;

    public FixedDocument Build()
    {
        var document = new FixedDocument();
        var remaining = new List<DeliveryNoteLine>(data.Lines);

        var first = remaining.Take(LinesPerSection).ToList();
        remaining = remaining.Skip(LinesPerSection).ToList();
        var totalPages = 1 + (remaining.Count > 0 ? (int)Math.Ceiling(remaining.Count / (double)LinesPerSection) : 0);

        AddPage(document, first, isFirst: true, isLast: remaining.Count == 0, pageNumber: 1, totalPages);

        var pageNumber = 2;
        while (remaining.Count > 0)
        {
            var chunk = remaining.Take(LinesPerSection).ToList();
            remaining = remaining.Skip(LinesPerSection).ToList();
            AddPage(document, chunk, isFirst: false, isLast: remaining.Count == 0, pageNumber: pageNumber++, totalPages);
        }

        return document;
    }

    private void AddPage(
        FixedDocument document, List<DeliveryNoteLine> lines, bool isFirst, bool isLast, int pageNumber, int totalPages)
    {
        var fixedPage = new FixedPage { Width = A4Width, Height = A4Height, Background = Brushes.White };

        var root = new StackPanel { Width = ContentWidth, Background = Brushes.White };
        for (var i = 0; i < SectionTitles.Length; i++)
        {
            if (i > 0)
            {
                // セクション間はミシン目の位置（罫線）を挟む。
                root.Children.Add(new Rectangle { Height = SectionGap, Fill = Brushes.Transparent });
                root.Children.Add(HLine(1.5));
                root.Children.Add(new Rectangle { Height = SectionGap, Fill = Brushes.Transparent });
            }

            root.Children.Add(BuildSection(SectionTitles[i], lines, isFirst, isLast, pageNumber, totalPages));
        }

        FixedPage.SetLeft(root, MarginX);
        FixedPage.SetTop(root, MarginY);
        fixedPage.Children.Add(root);

        // FixedPage は Show/ShowDialog のビジュアルツリーに乗らないため、
        // レイアウトを自分で確定させる必要がある（参考実装と同じ手順）。
        fixedPage.Measure(new Size(A4Width, A4Height));
        fixedPage.Arrange(new Rect(0, 0, A4Width, A4Height));
        fixedPage.UpdateLayout();

        var pageContent = new PageContent();
        ((IAddChild)pageContent).AddChild(fixedPage);
        document.Pages.Add(pageContent);
    }

    private FrameworkElement BuildSection(
        string title, List<DeliveryNoteLine> lines, bool isFirst, bool isLast, int pageNumber, int totalPages)
    {
        var section = new StackPanel { Width = ContentWidth, Height = SectionHeight };

        if (isFirst)
        {
            section.Children.Add(BuildSectionTitle(title, totalPages > 1 ? $"（{pageNumber}/{totalPages}）" : ""));
            section.Children.Add(HLine(1));
            section.Children.Add(BuildSectionInfoRow());
            section.Children.Add(BuildSectionSlipMeta());
            section.Children.Add(HLine(0.75));
        }
        else
        {
            section.Children.Add(BuildSectionCompactHeader(title, pageNumber, totalPages));
            section.Children.Add(HLine(0.75));
        }

        section.Children.Add(BuildSectionLinesTable(lines));

        if (isLast)
        {
            section.Children.Add(HLine(0.75));
            section.Children.Add(BuildSectionFooter());
        }

        return section;
    }

    private FrameworkElement BuildSectionTitle(string title, string pageLabel)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dateText = Tb(data.SlipDate.ToString("yyyy/MM/dd"), 9);
        dateText.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(dateText, 0);
        grid.Children.Add(dateText);

        var titleText = data.IssueCount > 0 ? $"{title}（再発行）" : title;
        var titleTb = Tb(titleText, 16, FontWeights.Bold, TextAlignment.Center);
        Grid.SetColumn(titleTb, 1);
        grid.Children.Add(titleTb);

        if (!string.IsNullOrEmpty(pageLabel))
        {
            var pageTb = Tb(pageLabel, 9, align: TextAlignment.Right);
            pageTb.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(pageTb, 2);
            grid.Children.Add(pageTb);
        }

        return grid;
    }

    private FrameworkElement BuildSectionInfoRow()
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

        // 縦幅が限られるため、通常版（BuildCustomerBlock）とは異なり郵便番号・住所を1行にまとめる。
        var leftPanel = new StackPanel();
        var addressParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(data.CustomerPostalCode))
        {
            addressParts.Add($"〒{data.CustomerPostalCode}");
        }
        if (!string.IsNullOrWhiteSpace(data.CustomerAddress1))
        {
            addressParts.Add(data.CustomerAddress1);
        }
        if (!string.IsNullOrWhiteSpace(data.CustomerAddress2))
        {
            addressParts.Add(data.CustomerAddress2);
        }
        if (addressParts.Count > 0)
        {
            leftPanel.Children.Add(Tb(string.Join("　", addressParts), 10));
        }
        leftPanel.Children.Add(Tb($"{data.CustomerName}　御中", 15, FontWeights.Bold));
        Grid.SetColumn(leftPanel, 0);
        grid.Children.Add(leftPanel);

        // 納品書は適格請求書として扱わない方針のため代表者印字は常に行わない（docs/report-spec.md 2-1節）。
        var box = BuildCompanyInfoBox(data.Company, printRepresentative: false, nameFontSize: 13);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);

        return grid;
    }

    private FrameworkElement BuildSectionSlipMeta()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 3) };
        row.Children.Add(Tb("伝票No.:　", 9, FontWeights.Bold));
        row.Children.Add(Tb(data.SalesSlipNumber, 9));

        if (!string.IsNullOrWhiteSpace(data.SlipRemarks))
        {
            row.Children.Add(new Rectangle { Width = 20, Fill = Brushes.Transparent });
            row.Children.Add(Tb("摘要:　", 9));
            row.Children.Add(Tb(data.SlipRemarks, 9));
        }

        return row;
    }

    private FrameworkElement BuildSectionCompactHeader(string title, int pageNumber, int totalPages)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        row.Children.Add(Tb($"{title}（続き）", 10, FontWeights.Bold));
        row.Children.Add(new Rectangle { Width = 16, Fill = Brushes.Transparent });
        row.Children.Add(Tb($"伝票No. {data.SalesSlipNumber}　{data.CustomerName}　御中", 9));
        row.Children.Add(new Rectangle
        {
            Width = 1, Fill = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch,
        });
        row.Children.Add(Tb($"{pageNumber}/{totalPages} ページ", 8, align: TextAlignment.Right));
        return row;
    }

    private FrameworkElement BuildSectionLinesTable(List<DeliveryNoteLine> lines)
    {
        var container = new StackPanel();

        container.Children.Add(BuildTableHeaderRow(_columns, CondensedTableHeaderHeight, CondensedFontSize));
        container.Children.Add(HLine(0.5));

        for (var i = 0; i < lines.Count; i++)
        {
            container.Children.Add(BuildTableRow(
                _columns, isHeader: false, AlternatingRowBackground(i), BuildLineCells(lines[i]),
                CondensedLineHeight, CondensedFontSize));
        }

        // 空行パディング。ミシン目位置を合わせるため、行数が足りなくても常にLinesPerSection行に揃える。
        for (var i = lines.Count; i < LinesPerSection; i++)
        {
            container.Children.Add(BuildTableRow(
                _columns, isHeader: false, Brushes.White, new string?[_columns.Count],
                CondensedLineHeight, CondensedFontSize));
        }

        return container;
    }

    private static string?[] BuildLineCells(DeliveryNoteLine line)
    {
        var slipTypePrefix = line.SlipType switch
        {
            SlipType.Return => "[返品] ",
            SlipType.Discount => "[値引] ",
            _ => "",
        };
        var reducedRatePrefix = line.TaxCategory == TaxCategory.Reduced ? "※ " : "";
        var productName = slipTypePrefix + reducedRatePrefix + line.ProductName;

        return
        [
            line.LineNumber.ToString(),
            line.ProductCode,
            productName,
            line.Quantity.ToString("N0"),
            line.UnitPrice.ToString("N4"),
            line.Amount.ToString("N0"),
            $"{line.TaxRate:N0}%",
            line.LineRemarks,
        ];
    }

    private FrameworkElement BuildSectionFooter()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        if (data.TaxUnit == TaxUnit.Invoice)
        {
            var totals = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            totals.Children.Add(HLine(1));
            totals.Children.Add(BuildTotalRow("税抜合計", data.TaxExcludedTotal, large: true));
            panel.Children.Add(totals);

            var note = Tb("※ 消費税は月次請求書にてご確認ください", 8);
            note.Margin = new Thickness(0, 4, 0, 0);
            note.Foreground = Brushes.Gray;
            panel.Children.Add(note);

            return panel;
        }

        if (data.Lines.Any(l => l.TaxCategory == TaxCategory.Reduced))
        {
            var reducedNote = Tb("※ は軽減税率（8%）対象商品です", 9);
            reducedNote.Margin = new Thickness(0, 0, 0, 4);
            reducedNote.Foreground = Brushes.Gray;
            panel.Children.Add(reducedNote);
        }

        if (data.TaxBreakdowns.Count > 0)
        {
            var breakdown = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var bucket in data.TaxBreakdowns)
            {
                breakdown.Children.Add(BuildBreakdownRow(bucket, fontSize: 9));
            }

            panel.Children.Add(breakdown);
            panel.Children.Add(HLine(0.5));
        }

        var totalsPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        totalsPanel.Children.Add(BuildTotalRow("税抜合計", data.TaxExcludedTotal, large: false));
        totalsPanel.Children.Add(BuildTotalRow("消費税合計", data.TaxTotal, large: false));
        totalsPanel.Children.Add(HLine(1));
        totalsPanel.Children.Add(BuildTotalRow("税込合計", data.GrandTotal, large: true));
        panel.Children.Add(totalsPanel);

        return panel;
    }
}
