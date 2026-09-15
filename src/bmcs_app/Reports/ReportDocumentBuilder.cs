using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Reports;

/// <summary>
/// 帳票の A4 <see cref="FixedDocument"/> を組み立てる基底クラス（TODO.md 10-3、帳票基盤）。
/// 参考実装（別リポジトリ <c>bmcs_app.Sales/Services/SalesPrintHelper.cs</c>）のうち、
/// 帳票種別に依存しない部分（A4寸法・改ページ・明細テーブル・共通描画ユーティリティ）だけを
/// テンプレートメソッドとして切り出したもの。派生クラスはヘッダー・フッター・列定義・行データのみを与える。
/// 改ページの分割計算そのものは <see cref="ReportPagination"/>（Domain、単体テスト済み）に委ねる。
/// </summary>
public abstract class ReportDocumentBuilder
{
    // ── A4 寸法（WPF device-independent units: 1/96 インチ。210mm/297mm を正確に変換した値）───
    // Width/Height は ReportPrintService が PrintQueue の PageSize を A4 に固定する際にも使うため public。
    public const double A4Width = 793.7008;
    public const double A4Height = 1122.5197;
    protected const double MarginX = 48.0;
    protected const double MarginY = 44.0;
    protected const double ContentWidth = A4Width - 2 * MarginX;
    protected const double ContentHeight = A4Height - 2 * MarginY;

    protected const double LineHeight = 21.0;
    protected const double TableHeaderHeight = 24.0;

    protected static readonly FontFamily JapaneseFont = new("Meiryo UI");
    private static readonly Brush TableHeaderBackground = new SolidColorBrush(Color.FromRgb(220, 220, 220));
    private static readonly Brush AltRowBackground = new SolidColorBrush(Color.FromRgb(248, 248, 248));
    private static readonly Brush ColumnSeparatorBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));

    /// <summary>1ページ目のヘッダー（タイトル・得意先・自社情報等）の高さの見積り。</summary>
    protected abstract double FullHeaderHeight { get; }

    /// <summary>2ページ目以降の続紙ヘッダーの高さの見積り。</summary>
    protected abstract double CompactHeaderHeight { get; }

    /// <summary>最終ページのフッター（税率別内訳・合計等）の高さの見積り。</summary>
    protected abstract double FooterHeight { get; }

    /// <summary>明細テーブルの列定義。幅0の列は残余幅になる（最大1列まで）。</summary>
    protected abstract IReadOnlyList<ReportColumn> Columns { get; }

    /// <summary>明細行の総数。</summary>
    protected abstract int LineCount { get; }

    /// <summary>1ページ目の先頭に出すフルヘッダー。</summary>
    protected abstract FrameworkElement BuildFullHeader();

    /// <summary>2ページ目以降の先頭に出す続紙ヘッダー。</summary>
    protected abstract FrameworkElement BuildCompactHeader(int pageNumber, int totalPages);

    /// <summary>最終ページの明細テーブルの下に出すフッター。</summary>
    protected abstract FrameworkElement BuildFooter();

    /// <summary><paramref name="lineIndex"/>（0始まり）の行を <see cref="Columns"/> と同じ順序のセル文字列で返す。</summary>
    protected abstract string?[] BuildLineCells(int lineIndex);

    public FixedDocument Build()
    {
        var linesOnFirstPage = Math.Max(1,
            (int)((ContentHeight - FullHeaderHeight - TableHeaderHeight - FooterHeight) / LineHeight));
        var linesOnLaterPages = Math.Max(1,
            (int)((ContentHeight - CompactHeaderHeight - TableHeaderHeight - FooterHeight) / LineHeight));

        var pageSplits = ReportPagination.Split(LineCount, linesOnFirstPage, linesOnLaterPages);
        var document = new FixedDocument();

        for (var i = 0; i < pageSplits.Count; i++)
        {
            var (startIndex, count) = pageSplits[i];
            AddPage(
                document, startIndex, count,
                isFirst: i == 0, isLast: i == pageSplits.Count - 1,
                pageNumber: i + 1, totalPages: pageSplits.Count);
        }

        return document;
    }

    private void AddPage(
        FixedDocument document, int startIndex, int count,
        bool isFirst, bool isLast, int pageNumber, int totalPages)
    {
        var fixedPage = new FixedPage
        {
            Width = A4Width,
            Height = A4Height,
            Background = Brushes.White,
        };

        var content = BuildPageContent(startIndex, count, isFirst, isLast, pageNumber, totalPages);
        FixedPage.SetLeft(content, MarginX);
        FixedPage.SetTop(content, MarginY);
        fixedPage.Children.Add(content);

        // FixedPage は Show/ShowDialog のビジュアルツリーに乗らないため、
        // レイアウトを自分で確定させる必要がある（参考実装と同じ手順）。
        fixedPage.Measure(new Size(A4Width, A4Height));
        fixedPage.Arrange(new Rect(0, 0, A4Width, A4Height));
        fixedPage.UpdateLayout();

        var pageContent = new PageContent();
        ((IAddChild)pageContent).AddChild(fixedPage);
        document.Pages.Add(pageContent);
    }

    private FrameworkElement BuildPageContent(
        int startIndex, int count, bool isFirst, bool isLast, int pageNumber, int totalPages)
    {
        var root = new StackPanel { Width = ContentWidth, Background = Brushes.White };

        root.Children.Add(isFirst ? BuildFullHeader() : BuildCompactHeader(pageNumber, totalPages));
        root.Children.Add(HLine(1));
        root.Children.Add(BuildLinesTable(startIndex, count));

        if (isLast)
        {
            root.Children.Add(HLine(1));
            root.Children.Add(BuildFooter());
        }

        return root;
    }

    private FrameworkElement BuildLinesTable(int startIndex, int count)
    {
        var container = new StackPanel();

        container.Children.Add(BuildTableRow(
            isHeader: true,
            background: TableHeaderBackground,
            cells: Columns.Select(c => (string?)c.Header).ToArray()));
        container.Children.Add(HLine(0.5));

        for (var i = 0; i < count; i++)
        {
            var cells = BuildLineCells(startIndex + i);
            var background = i % 2 == 0 ? Brushes.White : AltRowBackground;
            container.Children.Add(BuildTableRow(isHeader: false, background: background, cells: cells));
        }

        return container;
    }

    protected FrameworkElement BuildTableRow(bool isHeader, Brush background, string?[] cells)
    {
        var widths = ResolveColumnWidths();

        var grid = new Grid
        {
            Background = background,
            Height = isHeader ? TableHeaderHeight : LineHeight,
        };
        foreach (var width in widths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }

        for (var i = 0; i < Columns.Count; i++)
        {
            var text = i < cells.Length ? cells[i] ?? "" : "";
            var textBlock = new TextBlock
            {
                Text = text,
                FontFamily = JapaneseFont,
                FontSize = 9.0,
                FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                TextAlignment = Columns[i].ToTextAlignment(),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(3, 0, 3, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                // MahApps.Metro のテーマ前景色が継承されると印字が薄いグレーになるため明示的に黒にする。
                Foreground = Brushes.Black,
            };
            Grid.SetColumn(textBlock, i);
            grid.Children.Add(textBlock);

            if (i < Columns.Count - 1)
            {
                var separator = new Rectangle
                {
                    Width = 0.5,
                    Fill = ColumnSeparatorBrush,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                Grid.SetColumn(separator, i);
                grid.Children.Add(separator);
            }
        }

        return grid;
    }

    private double[] ResolveColumnWidths()
    {
        var fixedTotal = Columns.Where(c => c.Width > 0).Sum(c => c.Width);
        var starWidth = ContentWidth - fixedTotal;
        return Columns.Select(c => c.Width > 0 ? c.Width : starWidth).ToArray();
    }

    protected static TextBlock Tb(
        string text, double size = 10, FontWeight? weight = null, TextAlignment align = TextAlignment.Left)
        => new()
        {
            Text = text,
            FontFamily = JapaneseFont,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            TextAlignment = align,
            // MahApps.Metro のテーマ前景色が継承されると印字が薄いグレーになるため明示的に黒にする
            // （注記等でグレーにしたい場合は呼び出し元が Foreground を上書きする）。
            Foreground = Brushes.Black,
        };

    protected static Rectangle HLine(double thickness)
        => new()
        {
            Height = thickness,
            Fill = Brushes.Black,
            Margin = new Thickness(0, 2, 0, 2),
        };

    // ── 帳票2枚目（TODO.md 10-5、請求書・明細請求書）を書いた時点で共通部分の不足が判明した
    // ため基底へ引き上げた処理（10-3のコメントどおり）。DeliveryNoteDocumentBuilder もこれらを使う。

    /// <summary>宛先ブロック（郵便番号・住所・宛名＋敬称）。</summary>
    protected static FrameworkElement BuildCustomerBlock(
        string customerName, string? postalCode, string? address1, string? address2, string suffix = "御中")
    {
        var panel = new StackPanel();

        if (!string.IsNullOrWhiteSpace(postalCode))
        {
            panel.Children.Add(Tb($"〒 {postalCode}", 9));
        }

        if (!string.IsNullOrWhiteSpace(address1))
        {
            panel.Children.Add(Tb(address1, 9));
        }

        if (!string.IsNullOrWhiteSpace(address2))
        {
            panel.Children.Add(Tb(address2, 9));
        }

        panel.Children.Add(Tb($"{customerName}　{suffix}", 16, FontWeights.Bold));

        return panel;
    }

    /// <summary>
    /// 発行者情報ボックス（社名・住所・TEL/FAX・登録番号）。<paramref name="printRepresentative"/>が
    /// 真のときは「代表者　○○○○」＋押印用の空欄枠を追加する（得意先マスタ
    /// <c>print_representative_flag</c>、TODO.md 10-5）。納品書は適格請求書として扱わない方針
    /// のため常に偽で呼ぶ。
    /// </summary>
    protected static FrameworkElement BuildCompanyInfoBox(CompanyInfo company, bool printRepresentative)
    {
        var panel = new StackPanel();
        panel.Children.Add(Tb(company.CompanyName, 11, FontWeights.Bold, TextAlignment.Right));

        var address = string.Concat(company.Address1, company.Address2);
        if (!string.IsNullOrWhiteSpace(address))
        {
            panel.Children.Add(Tb(address, 8, align: TextAlignment.Right));
        }

        if (!string.IsNullOrWhiteSpace(company.PhoneNumber))
        {
            panel.Children.Add(Tb($"TEL: {company.PhoneNumber}", 8, align: TextAlignment.Right));
        }

        if (!string.IsNullOrWhiteSpace(company.FaxNumber))
        {
            panel.Children.Add(Tb($"FAX: {company.FaxNumber}", 8, align: TextAlignment.Right));
        }

        panel.Children.Add(new Rectangle { Height = 4, Fill = Brushes.Transparent });
        panel.Children.Add(Tb($"登録番号: {company.InvoiceRegistrationNumber}", 8, FontWeights.Bold, TextAlignment.Right));

        if (printRepresentative && !string.IsNullOrWhiteSpace(company.RepresentativeName))
        {
            panel.Children.Add(new Rectangle { Height = 6, Fill = Brushes.Transparent });

            var representativeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            representativeRow.Children.Add(Tb($"代表者　{company.RepresentativeName}", 9, align: TextAlignment.Right));
            representativeRow.Children.Add(new Rectangle { Width = 8, Fill = Brushes.Transparent });
            representativeRow.Children.Add(new Border
            {
                Width = 28,
                Height = 28,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
            });
            panel.Children.Add(representativeRow);
        }

        return new Border
        {
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            Child = panel,
        };
    }

    /// <summary>
    /// 振込先口座ブロック（請求書・明細請求書用。<c>bank_account.is_print_on_invoice</c>が
    /// 真の口座を<c>display_order</c>順に表示する。TODO.md 10-5）。
    /// </summary>
    protected static FrameworkElement BuildBankAccountsBlock(IReadOnlyList<BankAccount> accounts)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        if (accounts.Count == 0)
        {
            return panel;
        }

        panel.Children.Add(Tb("お振込先", 9, FontWeights.Bold));
        foreach (var account in accounts)
        {
            var typeLabel = account.AccountType == BankAccountType.Checking ? "当座" : "普通";
            panel.Children.Add(Tb(
                $"{account.BankName}　{account.BranchName}支店　{typeLabel}　{account.AccountNumber}　{account.AccountHolderName}",
                9));
        }

        return panel;
    }

    protected static FrameworkElement BuildBreakdownRow(TaxRateBucket bucket)
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

    protected static FrameworkElement BuildLabelValue(string label, decimal value)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Tb($"{label}:", 9));
        panel.Children.Add(new Rectangle { Width = 4, Fill = Brushes.Transparent });
        var valueText = Tb(value.ToString("N0"), 9, FontWeights.Bold, TextAlignment.Right);
        valueText.MinWidth = 80;
        panel.Children.Add(valueText);
        return panel;
    }

    protected static FrameworkElement BuildTotalRow(string label, decimal value, bool large)
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
