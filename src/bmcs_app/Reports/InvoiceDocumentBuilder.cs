using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using bmcs_app.Application.Billing;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Reports;

/// <summary>
/// 請求書のレイアウト（TODO.md 10-5）。締め得意先（請求単位／伝票単位）向け。前回請求額・入金額・
/// 今回売上額・消費税額・今回ご請求額の請求書サマリー（<c>billing</c>ヘッダーの本体データ）を
/// 明細テーブルの上に表示する。明細請求書と同様、複数の売上伝票にまたがるため「伝票No.」列を持つ。
/// 税率別内訳（フッター）は本請求期間の売上・消費税（<c>SalesAmount</c>／<c>TaxTotal</c>）に対する
/// もので、前回残高・入金を含む今回ご請求額（<c>CurrentBillingAmount</c>）とは別物であるため
/// 表示上も分離する。
/// </summary>
public sealed class InvoiceDocumentBuilder(InvoiceData data) : ReportDocumentBuilder
{
    private static readonly IReadOnlyList<ReportColumn> ColumnDefinitions =
    [
        new("伝票No.", 78, ReportColumnAlign.Left),
        new("商品コード", 78, ReportColumnAlign.Left),
        new("商品名", 0, ReportColumnAlign.Left),
        new("数量", 48, ReportColumnAlign.Right),
        new("単価", 68, ReportColumnAlign.Right),
        new("金額", 74, ReportColumnAlign.Right),
        new("税率", 38, ReportColumnAlign.Right),
        new("摘要", 70, ReportColumnAlign.Left),
    ];

    protected override double FullHeaderHeight => 340.0;
    protected override double CompactHeaderHeight => 34.0;
    protected override double FooterHeight => 220.0;
    protected override int LineCount => data.Lines.Count;

    protected override IReadOnlyList<ReportColumn> Columns => ColumnDefinitions;

    protected override string?[] BuildLineCells(int lineIndex)
    {
        var line = data.Lines[lineIndex];
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
            line.SalesSlipNumber,
            line.ProductCode,
            productName,
            line.Quantity.ToString("N0"),
            line.UnitPrice.ToString("N4"),
            line.Amount.ToString("N0"),
            $"{line.TaxRate:N0}%",
            line.LineRemarks,
        ];
    }

    protected override FrameworkElement BuildFullHeader()
    {
        var root = new StackPanel();

        root.Children.Add(BuildTitle());
        root.Children.Add(HLine(1.5));
        root.Children.Add(BuildInfoRow());
        root.Children.Add(BuildSummaryBlock());
        root.Children.Add(BuildSlipMeta());

        return root;
    }

    protected override FrameworkElement BuildCompactHeader(int pageNumber, int totalPages)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(Tb("請求書（続き）", 10, FontWeights.Bold));
        row.Children.Add(new System.Windows.Shapes.Rectangle { Width = 20, Fill = Brushes.Transparent });
        row.Children.Add(Tb($"請求No. {data.BillingNumber}　{data.CustomerName}　御中", 9));
        row.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = 1, Fill = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch,
        });
        row.Children.Add(Tb($"{pageNumber}/{totalPages} ページ", 8, align: TextAlignment.Right));
        return row;
    }

    private FrameworkElement BuildTitle()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dateText = Tb(data.BillingDate.ToString("yyyy/MM/dd"), 9);
        dateText.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(dateText, 0);
        grid.Children.Add(dateText);

        var title = Tb("請  求  書", 20, FontWeights.Bold, TextAlignment.Center);
        title.Margin = new Thickness(0, 0, 0, 4);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        return grid;
    }

    private FrameworkElement BuildInfoRow()
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        var leftPanel = BuildCustomerBlock(data.CustomerName, data.CustomerPostalCode, data.CustomerAddress1, data.CustomerAddress2);
        Grid.SetColumn(leftPanel, 0);
        grid.Children.Add(leftPanel);

        var box = BuildCompanyInfoBox(data.Company, data.PrintRepresentative);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);

        return grid;
    }

    /// <summary>
    /// 請求書サマリー（<c>billing</c>ヘッダーの本体データ）。
    /// 前回請求額－ご入金額＋今回売上額＋消費税額＝今回ご請求額（`docs/design_document.md` 9-3節）。
    /// </summary>
    private FrameworkElement BuildSummaryBlock()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        panel.Children.Add(BuildTotalRow("前回請求額", data.PreviousBalance, large: false));
        panel.Children.Add(BuildTotalRow("ご入金額", data.ReceiptAmount, large: false));
        panel.Children.Add(BuildTotalRow("今回売上額", data.SalesAmount, large: false));
        panel.Children.Add(BuildTotalRow("消費税額", data.TaxTotal, large: false));
        panel.Children.Add(HLine(1.5));
        panel.Children.Add(BuildTotalRow("今回ご請求額", data.CurrentBillingAmount, large: true));

        return panel;
    }

    private FrameworkElement BuildSlipMeta()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Tb("請求No.:　", 9, FontWeights.Bold));
        row.Children.Add(Tb(data.BillingNumber, 9));
        row.Children.Add(new System.Windows.Shapes.Rectangle { Width = 20, Fill = Brushes.Transparent });
        row.Children.Add(Tb("対象年月:　", 9, FontWeights.Bold));
        row.Children.Add(Tb($"{data.ClosingYearMonth[..4]}年{data.ClosingYearMonth[4..]}月分", 9));
        panel.Children.Add(row);

        return panel;
    }

    protected override FrameworkElement BuildFooter()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        if (data.TaxBreakdowns.Count > 0)
        {
            var breakdown = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var bucket in data.TaxBreakdowns)
            {
                breakdown.Children.Add(BuildBreakdownRow(bucket));
            }

            panel.Children.Add(breakdown);
            panel.Children.Add(HLine(0.5));
        }

        var totalsPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        totalsPanel.Children.Add(BuildTotalRow("今回売上額（税抜）", data.SalesAmount, large: false));
        totalsPanel.Children.Add(BuildTotalRow("消費税合計", data.TaxTotal, large: false));
        totalsPanel.Children.Add(HLine(1.5));
        totalsPanel.Children.Add(BuildTotalRow("今回ご請求額", data.CurrentBillingAmount, large: true));
        panel.Children.Add(totalsPanel);

        if (data.Lines.Any(l => l.TaxCategory == TaxCategory.Reduced))
        {
            var reducedNote = Tb("※ は軽減税率（8%）対象商品です", 7);
            reducedNote.Margin = new Thickness(0, 8, 0, 0);
            reducedNote.Foreground = Brushes.Gray;
            panel.Children.Add(reducedNote);
        }

        panel.Children.Add(BuildBankAccountsBlock(data.PrintBankAccounts));

        return panel;
    }
}
