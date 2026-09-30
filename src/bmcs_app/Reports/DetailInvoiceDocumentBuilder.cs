using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using bmcs_app.Application.Billing;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Reports;

/// <summary>
/// 明細請求書のレイアウト（TODO.md 10-5）。都度得意先（内税明細単位）向け。複数の売上伝票にまたがる
/// 明細を1枚にまとめるため、納品書には無い「伝票No.」列を持つ。適格請求書の記載事項
/// （登録番号・取引年月日・軽減税率対象品目の付記・税率ごとの対価額と適用税率・税率ごとの消費税額・
/// 交付を受ける者の名称）をすべて満たす。宛名は<c>AddresseeName</c>（都度入力のスナップショット、
/// C-9）を印字し、得意先マスタの登録名称は使わない。
/// </summary>
public sealed class DetailInvoiceDocumentBuilder(DetailInvoiceData data) : PagedReportDocumentBuilder
{
    private static readonly IReadOnlyList<ReportColumn> ColumnDefinitions =
    [
        new("伝票No.", 78, ReportColumnAlign.Left),
        new("商品コード", 78, ReportColumnAlign.Left),
        new("商品名", 0, ReportColumnAlign.Left),
        new("数量", 48, ReportColumnAlign.Right),
        new("単価(税込)", 68, ReportColumnAlign.Right),
        new("金額(税込)", 74, ReportColumnAlign.Right),
        new("税率", 38, ReportColumnAlign.Right),
        new("摘要", 70, ReportColumnAlign.Left),
    ];

    // 発行者情報ボックス直下に追加した振込先口座ボックス分を加算する
    // （BillingBankAccountsBoxHeightEstimate、2026-09-29、振込先の得意先単位印字対応）。
    protected override double FullHeaderHeight => 260.0 + BillingBankAccountsBoxHeightEstimate;
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
        root.Children.Add(BuildSlipMeta());

        return root;
    }

    protected override FrameworkElement BuildCompactHeader(int pageNumber, int totalPages)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(Tb("請求書（続き）", 10, FontWeights.Bold));
        row.Children.Add(new System.Windows.Shapes.Rectangle { Width = 20, Fill = Brushes.Transparent });
        row.Children.Add(Tb($"請求書No. {data.DetailInvoiceNumber}　{data.AddresseeName}　御中", 9));
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

        var dateText = Tb(data.IssueDate.ToString("yyyy/MM/dd"), 9);
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

        var leftPanel = BuildCustomerBlock(data.AddresseeName, data.CustomerPostalCode, data.CustomerAddress1, data.CustomerAddress2);
        Grid.SetColumn(leftPanel, 0);
        grid.Children.Add(leftPanel);

        var rightPanel = new StackPanel();
        rightPanel.Children.Add(BuildCompanyInfoBox(data.Company, data.PrintRepresentative));
        rightPanel.Children.Add(BuildBillingBankAccountsBox(data.BillingBankAccounts));
        Grid.SetColumn(rightPanel, 1);
        grid.Children.Add(rightPanel);

        return grid;
    }

    private FrameworkElement BuildSlipMeta()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Tb("請求書No.:　", 9, FontWeights.Bold));
        row.Children.Add(Tb(data.DetailInvoiceNumber, 9));
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
        totalsPanel.Children.Add(BuildTotalRow("税抜合計", data.TaxExcludedTotal, large: false));
        totalsPanel.Children.Add(BuildTotalRow("消費税合計", data.TaxTotal, large: false));
        totalsPanel.Children.Add(HLine(1.5));
        totalsPanel.Children.Add(BuildTotalRow("税込合計", data.GrandTotal, large: true));
        panel.Children.Add(totalsPanel);

        if (data.Lines.Any(l => l.TaxCategory == TaxCategory.Reduced))
        {
            var reducedRate = data.Lines.First(l => l.TaxCategory == TaxCategory.Reduced).TaxRate;
            var reducedNote = Tb($"※ は軽減税率（{reducedRate:0.##}%）対象商品です", 7);
            reducedNote.Margin = new Thickness(0, 8, 0, 0);
            reducedNote.Foreground = Brushes.Gray;
            panel.Children.Add(reducedNote);
        }

        return panel;
    }
}
