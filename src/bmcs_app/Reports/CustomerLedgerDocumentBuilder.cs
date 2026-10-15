using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using bmcs_app.Application.Ledger;
using bmcs_app.Converters;
using bmcs_app.Domain.Enums;
using bmcs_app.ViewModels.Ledger;

namespace bmcs_app.Reports;

/// <summary>
/// 得意先元帳（得意先元帳画面から印刷。docs/report-spec.md 2-3）。選んだ得意先・期間の元帳を時系列に並べ、
/// 毎ページのフッターに「前頁繰越／次頁繰越」を、最終ページに期間の合計と期末残高を出す。
/// 請求集約元（取引履歴のみモード）は残高を持たないため、残高列・繰越を出さず売上額の合計だけを印字する。
/// 行の文言（区分・摘要）は画面と同じ <see cref="CustomerLedgerLineViewModel"/> を使う。
/// </summary>
public sealed class CustomerLedgerDocumentBuilder(CustomerLedgerReportData data) : PagedReportDocumentBuilder
{
    private const double BalanceColumnWidth = 74;

    private readonly bool _historyOnly = data.Ledger.IsTransactionHistoryOnly;

    private readonly IReadOnlyList<CustomerLedgerLineViewModel> _lines =
        data.Ledger.Entries.Select(e => new CustomerLedgerLineViewModel(e, data.Ledger.IsTransactionHistoryOnly)).ToList();

    private readonly IReadOnlyList<(ReportColumn Column, Func<CustomerLedgerLineViewModel, string?> Cell)> _columns =
        BuildColumns(
            data.Ledger.IsTransactionHistoryOnly,
            // 請求集約先の元帳はグループ内の複数得意先の伝票が混ざるため、得意先列を出す。
            data.Ledger.Entries.Any(e => e.CustomerCode is { } code && code != data.CustomerCode));

    private static readonly EnumDisplayConverter EnumDisplay = new();

    /// <summary>
    /// 画面（得意先元帳）と同じ項目を並べる。横向きで幅を取れるため省略はしない。
    /// 取引履歴のみ（請求集約元）は入金・残高の列を出さない。商品名／摘要と備考が残余幅を分け合う。
    /// </summary>
    private static List<(ReportColumn, Func<CustomerLedgerLineViewModel, string?>)> BuildColumns(bool historyOnly, bool multiCustomer)
    {
        List<(ReportColumn, Func<CustomerLedgerLineViewModel, string?>)> columns =
        [
            (new("区分", 40, ReportColumnAlign.Left), l => l.KindLabel),
            (new("伝票日付", 56, ReportColumnAlign.Left),
                l => l.Entry.Kind == LedgerEntryKind.OpeningBalance ? null : l.Entry.EntryDate.ToString("yyyy/MM/dd")),
        ];

        if (multiCustomer)
        {
            columns.Add((new("得意先", 90, ReportColumnAlign.Left), l => l.CustomerLabel));
        }

        columns.AddRange(
        [
            (new("伝票No", 62, ReportColumnAlign.Left), l => l.Entry.SalesSlipNumber),
            (new("行", 24, ReportColumnAlign.Right), l => l.Entry.SalesLineNumber?.ToString()),
            (new("商品コード", 60, ReportColumnAlign.Left), l => l.Entry.ProductCode),
            (new("商品名／摘要", 0, ReportColumnAlign.Left), l => l.Description),
            (new("課税区分", 46, ReportColumnAlign.Left),
                l => l.Entry.TaxCategory is { } c ? (string)EnumDisplay.Convert(c, typeof(string), null!, CultureInfo.CurrentCulture) : null),
            (new("数量", 44, ReportColumnAlign.Right), l => l.Entry.Quantity?.ToString("#,0.###")),
            (new("単価", 52, ReportColumnAlign.Right), l => l.Entry.UnitPrice?.ToString("N0")),
            (new("売上額", 66, ReportColumnAlign.Right), l => l.Entry.DebitAmount?.ToString("N0")),
            (new("請求", 26, ReportColumnAlign.Left), l => l.BilledMark),
        ]);

        if (!historyOnly)
        {
            columns.AddRange(
            [
                (new("入金日付", 56, ReportColumnAlign.Left), l => l.Entry.ReceiptDate?.ToString("yyyy/MM/dd")),
                (new("入金No", 62, ReportColumnAlign.Left), l => l.Entry.ReceiptSlipNumber),
                (new("入金額", 66, ReportColumnAlign.Right), l => l.Entry.ReceiptAmount?.ToString("N0")),
                (new("残高", BalanceColumnWidth, ReportColumnAlign.Right), l => l.BalanceDisplay?.ToString("N0")),
            ]);
        }

        columns.Add((new("備考", 0, ReportColumnAlign.Left), l => l.Entry.Remarks));

        // 残余幅（商品名／摘要と備考）を 7:3 で分ける。
        var remaining = LandscapeContentWidth - columns.Sum(c => c.Item1.Width);
        var remarksWidth = Math.Floor(remaining * 0.3);
        return columns
            .Select(c => c.Item1.Header switch
            {
                "備考" => (c.Item1 with { Width = remarksWidth }, c.Item2),
                "商品名／摘要" => (c.Item1 with { Width = remaining - remarksWidth }, c.Item2),
                _ => c,
            })
            .ToList();
    }

    protected override bool IsLandscape => true;
    protected override double? TableFontSize => 8.0;
    protected override double FullHeaderHeight => 76.0;
    protected override double CompactHeaderHeight => 34.0;
    protected override double FooterHeight => 40.0;
    protected override double PageFooterHeight => _historyOnly ? 0 : 26.0;
    protected override int LineCount => _lines.Count;

    protected override IReadOnlyList<ReportColumn> Columns => _columns.Select(c => c.Column).ToList();

    protected override string?[] BuildLineCells(int lineIndex)
    {
        var line = _lines[lineIndex];
        return _columns.Select(c => c.Cell(line)).ToArray();
    }

    protected override FrameworkElement BuildFullHeader()
    {
        var root = new StackPanel();

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        for (var i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var title = Tb("得意先元帳", 18, FontWeights.Bold, TextAlignment.Center);
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
        root.Children.Add(Tb($"{data.CustomerCode}　{data.CustomerName}", 12, FontWeights.Bold));
        root.Children.Add(Tb(
            $"期間 {data.PeriodFrom:yyyy/MM/dd} ～ {data.PeriodTo:yyyy/MM/dd}　（単位：円）", 8));

        return root;
    }

    protected override FrameworkElement BuildCompactHeader(int pageNumber, int totalPages)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = Tb($"得意先元帳（続き）　{data.CustomerCode}　{data.CustomerName}", 10, FontWeights.Bold);
        Grid.SetColumn(left, 0);
        row.Children.Add(left);

        var pageText = Tb($"{pageNumber}/{totalPages} ページ", 8, align: TextAlignment.Right);
        Grid.SetColumn(pageText, 1);
        row.Children.Add(pageText);

        return row;
    }

    /// <summary>
    /// 前頁繰越＝このページの直前の行の残高、次頁繰越＝このページ最終行の残高（最終ページは出さない）。
    /// 1ページ目の前頁繰越は無し（先頭行が前月繰越行のため）。
    /// </summary>
    protected override FrameworkElement? BuildPageFooter(int startIndex, int count, bool isLast)
    {
        if (_historyOnly)
        {
            return null;
        }

        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (startIndex > 0)
        {
            row.Children.Add(Tb($"前頁繰越　{_lines[startIndex - 1].Entry.Balance:N0}", 9, FontWeights.Bold));
        }

        if (!isLast)
        {
            var next = Tb($"次頁繰越　{_lines[startIndex + count - 1].Entry.Balance:N0}", 9, FontWeights.Bold, TextAlignment.Right);
            Grid.SetColumn(next, 1);
            row.Children.Add(next);
        }

        return row;
    }

    protected override FrameworkElement BuildFooter()
    {
        var ledger = data.Ledger;
        var panel = new UniformGrid { Rows = 1, Margin = new Thickness(0, 6, 0, 0) };

        if (_historyOnly)
        {
            panel.Columns = 1;
            panel.Children.Add(BuildLabelValue("売上額合計", ledger.SalesTotal));
            return panel;
        }

        panel.Columns = 5;
        panel.Children.Add(BuildLabelValue("前月繰越", ledger.OpeningBalance));
        panel.Children.Add(BuildLabelValue("売上額", ledger.SalesTotal));
        panel.Children.Add(BuildLabelValue("消費税", ledger.TaxTotal));
        panel.Children.Add(BuildLabelValue("入金額", ledger.ReceiptTotal));
        panel.Children.Add(BuildLabelValue("期末残高", ledger.ClosingBalance));
        return panel;
    }
}
