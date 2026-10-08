using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using bmcs_app.Application.Billing;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Reports;

/// <summary>
/// 請求書のレイアウト。締め得意先（請求単位／伝票単位）向け。前回請求額・
/// 入金額・今回売上額・消費税額・今回ご請求額の請求書サマリー（<c>billing</c>ヘッダーの本体データ）を
/// 明細テーブルの上に表示する。明細請求書と同様、複数の売上伝票にまたがるため「伝票No.」列を持つ。
/// 税率別内訳（フッター）は本請求期間の売上・消費税（<c>SalesAmount</c>／<c>TaxTotal</c>）に対する
/// もので、前回残高・入金を含む今回ご請求額（<c>CurrentBillingAmount</c>）とは別物であるため
/// 表示上も分離する。
///
/// 親子請求（請求集約）: <c>data.Lines</c>が複数得意先にまたがる場合
/// （<see cref="InvoiceService"/>が得意先コード順に整列済み）、<see cref="InvoiceReportRowBuilder"/>
/// （Domain純粋関数）で得意先ごとの見出し行・小計行を挟んだ表示順を組み立てる。単独得意先の場合は
/// 明細のみが返るため、既存の単独得意先の帳票はバイト単位で不変（docs/report-spec.md 2-2-1節）。
/// </summary>
public sealed class InvoiceDocumentBuilder(InvoiceData data) : PagedReportDocumentBuilder
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

    private readonly IReadOnlyList<InvoiceReportRow> _rows = InvoiceReportRowBuilder.Build(
        data.Lines.Select(l => (l.CustomerCode, l.CustomerName, l.Amount)).ToList());

    /// <summary>請求集約先の請求書（配下の請求集約元の分も合算されている）かどうか。</summary>
    private bool IsAggregated => _rows.Any(r => r.Kind == InvoiceReportRowKind.GroupHeader);

    /// <summary>
    /// 配下の請求集約元（自分自身＝請求集約先を除く）の件数。請求集約先自身の売上が
    /// 今回の請求期間に無い場合（配下の分だけで請求データが作られたケース）もありうるため、
    /// 「見出し行の総数」からではなく「請求集約先自身のコードが明細に含まれているか」で判定する。
    /// </summary>
    private int AggregatedChildCount
    {
        get
        {
            var groupCount = _rows.Count(r => r.Kind == InvoiceReportRowKind.GroupHeader);
            var rootHasOwnLines = data.Lines.Any(l => l.CustomerCode == data.CustomerCode);
            return rootHasOwnLines ? groupCount - 1 : groupCount;
        }
    }

    // 集約時は続紙ヘッダーに「請求集約元: N社」の1行が増える分だけフルヘッダーの高さを増やす
    // （可変にし忘れると最終ページでフッターが本文と重なる。docs/report-spec.md 2-2-1節）。
    // 発行者情報ボックス直下に追加した振込先口座ボックス分を加算する
    // （BillingBankAccountsBoxHeightEstimate、振込先の得意先単位印字に対応）。
    protected override double FullHeaderHeight => (IsAggregated ? 358.0 : 340.0) + BillingBankAccountsBoxHeightEstimate;
    protected override double CompactHeaderHeight => 34.0;
    protected override double FooterHeight => 220.0;
    protected override int LineCount => _rows.Count;

    protected override IReadOnlyList<ReportColumn> Columns => ColumnDefinitions;

    protected override bool IsGroupMarkerRow(int lineIndex)
        => _rows[lineIndex].Kind != InvoiceReportRowKind.Line;

    protected override bool IsPageBreakSensitive(int lineIndex)
        => _rows[lineIndex].Kind == InvoiceReportRowKind.GroupHeader;

    protected override string?[] BuildLineCells(int lineIndex)
    {
        var row = _rows[lineIndex];
        return row.Kind switch
        {
            InvoiceReportRowKind.GroupHeader => [null, null, row.Label, null, null, null, null, null],
            InvoiceReportRowKind.GroupSubtotal =>
                [null, null, row.Label, null, null, row.SubtotalAmount!.Value.ToString("N0"), null, null],
            _ => BuildDetailLineCells(data.Lines[row.SourceLineIndex!.Value]),
        };
    }

    private static string?[] BuildDetailLineCells(InvoiceLine line)
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

        var rightPanel = new StackPanel();
        rightPanel.Children.Add(BuildCompanyInfoBox(data.Company, data.PrintRepresentative));
        rightPanel.Children.Add(BuildBillingBankAccountsBox(data.BillingBankAccounts));
        Grid.SetColumn(rightPanel, 1);
        grid.Children.Add(rightPanel);

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

        if (IsAggregated)
        {
            var aggregationRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            aggregationRow.Children.Add(Tb("請求集約元:　", 9, FontWeights.Bold));
            aggregationRow.Children.Add(Tb($"{AggregatedChildCount}社", 9));
            panel.Children.Add(aggregationRow);
        }

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
            var reducedRate = data.Lines.First(l => l.TaxCategory == TaxCategory.Reduced).TaxRate;
            var reducedNote = Tb($"※ は軽減税率（{reducedRate:0.##}%）対象商品です", 7);
            reducedNote.Margin = new Thickness(0, 8, 0, 0);
            reducedNote.Foreground = Brushes.Gray;
            panel.Children.Add(reducedNote);
        }

        return panel;
    }
}
