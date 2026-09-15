using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using bmcs_app.Application.Sales;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Reports;

/// <summary>
/// 納品書のレイアウト（TODO.md 10-4）。参考実装（別リポジトリ
/// <c>bmcs_app.Sales/Services/SalesPrintHelper.cs</c>）のレイアウトを踏襲するが、
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

    protected override double FullHeaderHeight => 240.0;
    protected override double CompactHeaderHeight => 34.0;
    protected override double FooterHeight => 160.0;
    protected override int LineCount => data.Lines.Count;

    protected override IReadOnlyList<ReportColumn> Columns => _columns;

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
            line.LineNumber.ToString(),
            line.ProductCode,
            productName,
            line.Quantity.ToString("N3"),
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
        row.Children.Add(Tb("納品書（続き）", 10, FontWeights.Bold));
        row.Children.Add(new System.Windows.Shapes.Rectangle { Width = 20, Fill = Brushes.Transparent });
        row.Children.Add(Tb($"伝票No. {data.SalesSlipNumber}　{data.CustomerName}　御中", 9));
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

        var dateText = Tb(data.SlipDate.ToString("yyyy/MM/dd"), 9);
        dateText.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(dateText, 0);
        grid.Children.Add(dateText);

        var title = Tb(data.IssueCount > 0 ? "納  品  書　（再発行）" : "納  品  書", 20, FontWeights.Bold, TextAlignment.Center);
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

        var leftPanel = new StackPanel();
        if (!string.IsNullOrWhiteSpace(data.CustomerPostalCode))
        {
            leftPanel.Children.Add(Tb($"〒 {data.CustomerPostalCode}", 9));
        }

        if (!string.IsNullOrWhiteSpace(data.CustomerAddress1))
        {
            leftPanel.Children.Add(Tb(data.CustomerAddress1, 9));
        }

        if (!string.IsNullOrWhiteSpace(data.CustomerAddress2))
        {
            leftPanel.Children.Add(Tb(data.CustomerAddress2, 9));
        }

        leftPanel.Children.Add(Tb($"{data.CustomerName}　御中", 16, FontWeights.Bold));
        Grid.SetColumn(leftPanel, 0);
        grid.Children.Add(leftPanel);

        var rightPanel = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        var company = data.Company;
        rightPanel.Children.Add(Tb(company.CompanyName, 11, FontWeights.Bold, TextAlignment.Right));
        var address = string.Concat(company.Address1, company.Address2);
        if (!string.IsNullOrWhiteSpace(address))
        {
            rightPanel.Children.Add(Tb(address, 8, align: TextAlignment.Right));
        }

        if (!string.IsNullOrWhiteSpace(company.PhoneNumber))
        {
            rightPanel.Children.Add(Tb($"TEL: {company.PhoneNumber}", 8, align: TextAlignment.Right));
        }

        if (!string.IsNullOrWhiteSpace(company.FaxNumber))
        {
            rightPanel.Children.Add(Tb($"FAX: {company.FaxNumber}", 8, align: TextAlignment.Right));
        }

        rightPanel.Children.Add(new System.Windows.Shapes.Rectangle { Height = 4, Fill = Brushes.Transparent });
        rightPanel.Children.Add(Tb($"登録番号: {company.InvoiceRegistrationNumber}", 8, FontWeights.Bold, TextAlignment.Right));

        var box = new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            Child = rightPanel,
        };
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);

        return grid;
    }

    private FrameworkElement BuildSlipMeta()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Tb("伝票No.:　", 9, FontWeights.Bold));
        row.Children.Add(Tb(data.SalesSlipNumber, 9));
        panel.Children.Add(row);

        if (!string.IsNullOrWhiteSpace(data.SlipRemarks))
        {
            var remarksRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            remarksRow.Children.Add(Tb("摘要:　", 9));
            remarksRow.Children.Add(Tb(data.SlipRemarks, 9));
            panel.Children.Add(remarksRow);
        }

        return panel;
    }

    protected override FrameworkElement BuildFooter()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        if (data.TaxUnit == TaxUnit.Invoice)
        {
            var totals = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            totals.Children.Add(HLine(1.5));
            totals.Children.Add(BuildTotalRow("税抜合計", data.TaxExcludedTotal, large: true));
            panel.Children.Add(totals);

            var note = Tb("※ 消費税は月次請求書にてご確認ください", 7);
            note.Margin = new Thickness(0, 8, 0, 0);
            note.Foreground = Brushes.Gray;
            panel.Children.Add(note);

            return panel;
        }

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
            var reducedNote = Tb("※ は軽減税率（8%）対象商品です", 7);
            reducedNote.Margin = new Thickness(0, 8, 0, 0);
            reducedNote.Foreground = Brushes.Gray;
            panel.Children.Add(reducedNote);
        }

        return panel;
    }

    private FrameworkElement BuildBreakdownRow(TaxRateBucket bucket)
    {
        var label = bucket.TaxCategory switch
        {
            TaxCategory.Reduced => $"{bucket.TaxRate:N0}%対象（軽減税率）",
            TaxCategory.TaxExempt => "非課税",
            _ => $"{bucket.TaxRate:N0}%対象",
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        var labelText = Tb($"※ {label}", 9);
        Grid.SetColumn(labelText, 0);
        row.Children.Add(labelText);

        var taxable = BuildLabelValue("税抜金額", bucket.TaxableAmount);
        Grid.SetColumn(taxable, 1);
        row.Children.Add(taxable);

        var tax = BuildLabelValue("消費税", bucket.TaxAmount);
        Grid.SetColumn(tax, 3);
        row.Children.Add(tax);

        return row;
    }

    private FrameworkElement BuildLabelValue(string label, decimal value)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Tb($"{label}:", 9));
        panel.Children.Add(new System.Windows.Shapes.Rectangle { Width = 4, Fill = Brushes.Transparent });
        var valueText = Tb(value.ToString("N0"), 9, FontWeights.Bold, TextAlignment.Right);
        valueText.MinWidth = 80;
        panel.Children.Add(valueText);
        return panel;
    }

    private FrameworkElement BuildTotalRow(string label, decimal value, bool large)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        var labelText = Tb(label, large ? 10.0 : 9.0, large ? FontWeights.Bold : FontWeights.Normal);
        labelText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(labelText, 0);
        grid.Children.Add(labelText);

        var valueText = Tb(
            value.ToString("N0"), large ? 14.0 : 10.0, large ? FontWeights.Bold : FontWeights.Normal, TextAlignment.Right);
        valueText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);

        return grid;
    }
}
