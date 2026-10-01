using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using bmcs_app.Application.Closing;

namespace bmcs_app.Reports;

/// <summary>
/// 売掛金残高一覧表（月次締め処理画面から印刷。docs/report-spec.md 2-4）。選んだ年月の確定済み
/// 月次締めを得意先コード順に全件並べ、前月残高・入金額・売上額・消費税・当月残高と、最終ページに合計を出す。
/// 請求集約元の行（前月残高・入金額・消費税・当月残高が0で保存されている）は、得意先名に「※」を付けて
/// 売上額だけを印字し、合計からは除く（請求集約先の行に含まれており、足すと二重に数えるため）。
/// </summary>
public sealed class ReceivablesBalanceDocumentBuilder(ReceivablesBalanceReportData data) : PagedReportDocumentBuilder
{
    private const string BillingChildMark = "※ ";

    private static readonly IReadOnlyList<ReportColumn> ColumnDefinitions =
    [
        new("得意先コード", 80, ReportColumnAlign.Left),
        new("得意先名", 0, ReportColumnAlign.Left),
        new("前月残高", 90, ReportColumnAlign.Right),
        new("入金額", 90, ReportColumnAlign.Right),
        new("売上額", 90, ReportColumnAlign.Right),
        new("消費税", 90, ReportColumnAlign.Right),
        new("当月残高", 90, ReportColumnAlign.Right),
    ];

    protected override double FullHeaderHeight => 80.0;
    protected override double CompactHeaderHeight => 34.0;

    // 合計行（TableHeaderHeight）＋注記（請求集約元がある場合のみ）＋余白。
    protected override double FooterHeight => 70.0;
    protected override int LineCount => data.Rows.Count;

    protected override IReadOnlyList<ReportColumn> Columns => ColumnDefinitions;

    protected override string?[] BuildLineCells(int lineIndex)
    {
        var row = data.Rows[lineIndex];
        if (row.IsBillingChild)
        {
            return [row.CustomerCode, BillingChildMark + row.CustomerName, null, null, row.SalesAmount.ToString("N0"), null, null];
        }

        return
        [
            row.CustomerCode,
            row.CustomerName,
            row.PreviousBalance.ToString("N0"),
            row.ReceiptAmount.ToString("N0"),
            row.SalesAmount.ToString("N0"),
            row.TaxAmount.ToString("N0"),
            row.ClosingBalance.ToString("N0"),
        ];
    }

    protected override FrameworkElement BuildFullHeader()
    {
        var root = new StackPanel();

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        for (var i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var period = Tb($"{data.Year}年{data.Month:00}月分", 11, FontWeights.Bold);
        period.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(period, 0);
        grid.Children.Add(period);

        var title = Tb("売掛金残高一覧表", 18, FontWeights.Bold, TextAlignment.Center);
        title.Margin = new Thickness(0, 0, 0, 4);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var issue = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        if (!string.IsNullOrWhiteSpace(data.CompanyName))
        {
            issue.Children.Add(Tb(data.CompanyName, 9, FontWeights.Bold, TextAlignment.Right));
        }

        issue.Children.Add(Tb($"発行日 {DateTime.Today:yyyy/MM/dd}", 8, align: TextAlignment.Right));
        Grid.SetColumn(issue, 2);
        grid.Children.Add(issue);

        root.Children.Add(grid);
        root.Children.Add(Tb(
            $"集計期間 {data.PeriodFrom:yyyy/MM/dd} ～ {data.PeriodTo:yyyy/MM/dd}　（単位：円）", 8));

        return root;
    }

    protected override FrameworkElement BuildCompactHeader(int pageNumber, int totalPages)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = Tb($"売掛金残高一覧表（続き）　{data.Year}年{data.Month:00}月分", 10, FontWeights.Bold);
        Grid.SetColumn(left, 0);
        row.Children.Add(left);

        var pageText = Tb($"{pageNumber}/{totalPages} ページ", 8, align: TextAlignment.Right);
        Grid.SetColumn(pageText, 1);
        row.Children.Add(pageText);

        return row;
    }

    protected override FrameworkElement BuildFooter()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        var total = data.Rows.Count(r => !r.IsBillingChild);
        panel.Children.Add(BuildTableRow(
            Columns, isHeader: true, Brushes.Transparent,
            [
                null,
                $"合計（{total}件）",
                data.TotalPreviousBalance.ToString("N0"),
                data.TotalReceiptAmount.ToString("N0"),
                data.TotalSalesAmount.ToString("N0"),
                data.TotalTaxAmount.ToString("N0"),
                data.TotalClosingBalance.ToString("N0"),
            ]));

        if (data.HasBillingChild)
        {
            var note = Tb("※ は請求集約元です。残高等は請求集約先に含まれるため、合計には含めていません。", 7);
            note.Margin = new Thickness(0, 6, 0, 0);
            note.Foreground = Brushes.Gray;
            panel.Children.Add(note);
        }

        return panel;
    }
}
