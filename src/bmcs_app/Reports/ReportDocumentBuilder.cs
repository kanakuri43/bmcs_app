using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Reports;

/// <summary>
/// 帳票の A4 描画プリミティブ（TODO.md 10-3、帳票基盤。2026-09-17、納品書3段複写対応で分割）。
/// 参考実装（別リポジトリ <c>bmcs_app.Sales/Services/SalesPrintHelper.cs</c>）のうち、
/// 帳票種別に依存しない部分（A4寸法・明細テーブル1行の描画・共通描画ユーティリティ）だけを
/// 切り出したもの。改ページを伴う単一フロー帳票（請求書・明細請求書）は
/// <see cref="PagedReportDocumentBuilder"/> がこれを継承してテンプレートメソッドを提供する。
/// 納品書（3段複写、ミシン目入りA4）のように改ページの考え方自体が異なる帳票は、
/// <see cref="DeliveryNoteDocumentBuilder"/> のように本クラスを直接継承し、
/// ここにある部品だけを使って独自の組み立てを行う。
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

    /// <summary>
    /// 明細テーブル1行分（ヘッダー行含む）を描画する。<paramref name="rowHeight"/>／
    /// <paramref name="fontSize"/> を省略すると、通常の単一フロー帳票の既定値
    /// （<see cref="TableHeaderHeight"/>／<see cref="LineHeight"/>／9pt）を使う。
    /// 納品書の3段複写のような縮小レイアウトは明示的に指定する。
    /// </summary>
    protected static FrameworkElement BuildTableRow(
        IReadOnlyList<ReportColumn> columns, bool isHeader, Brush background, string?[] cells,
        double? rowHeight = null, double? fontSize = null)
    {
        var widths = ResolveColumnWidths(columns);

        var grid = new Grid
        {
            Background = background,
            Height = rowHeight ?? (isHeader ? TableHeaderHeight : LineHeight),
        };
        foreach (var width in widths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var text = i < cells.Length ? cells[i] ?? "" : "";
            var textBlock = new TextBlock
            {
                Text = text,
                FontFamily = JapaneseFont,
                FontSize = fontSize ?? 9.0,
                FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                TextAlignment = columns[i].ToTextAlignment(),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(3, 0, 3, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                // MahApps.Metro のテーマ前景色が継承されると印字が薄いグレーになるため明示的に黒にする。
                Foreground = Brushes.Black,
            };
            Grid.SetColumn(textBlock, i);
            grid.Children.Add(textBlock);

            if (i < columns.Count - 1)
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

    protected static FrameworkElement BuildTableHeaderRow(
        IReadOnlyList<ReportColumn> columns, double? rowHeight = null, double? fontSize = null)
        => BuildTableRow(
            columns, isHeader: true, TableHeaderBackground,
            columns.Select(c => (string?)c.Header).ToArray(), rowHeight, fontSize);

    /// <summary>データ行の背景色（縞模様）。<paramref name="rowIndexInPage"/>は0始まり。</summary>
    protected static Brush AlternatingRowBackground(int rowIndexInPage)
        => rowIndexInPage % 2 == 0 ? Brushes.White : AltRowBackground;

    private static double[] ResolveColumnWidths(IReadOnlyList<ReportColumn> columns)
    {
        var fixedTotal = columns.Where(c => c.Width > 0).Sum(c => c.Width);
        var starWidth = ContentWidth - fixedTotal;
        return columns.Select(c => c.Width > 0 ? c.Width : starWidth).ToArray();
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
        string customerName, string? postalCode, string? address1, string? address2,
        string suffix = "御中", double nameFontSize = 16)
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

        panel.Children.Add(Tb($"{customerName}　{suffix}", nameFontSize, FontWeights.Bold));

        return panel;
    }

    /// <summary>
    /// 発行者情報ボックス（社名・住所・TEL/FAX・登録番号）。<paramref name="printRepresentative"/>が
    /// 真のときは「代表者　○○○○」＋押印用の空欄枠を追加する（得意先マスタ
    /// <c>print_representative_flag</c>、TODO.md 10-5）。納品書は適格請求書として扱わない方針
    /// のため常に偽で呼ぶ。
    /// </summary>
    protected static FrameworkElement BuildCompanyInfoBox(
        CompanyInfo company, bool printRepresentative, double nameFontSize = 11)
    {
        var panel = new StackPanel();
        panel.Children.Add(Tb(company.CompanyName, nameFontSize, FontWeights.Bold, TextAlignment.Right));

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

    protected static FrameworkElement BuildBreakdownRow(TaxRateBucket bucket, double fontSize = 9)
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

        var labelText = Tb($"※ {label}", fontSize);
        Grid.SetColumn(labelText, 0);
        row.Children.Add(labelText);

        var taxable = BuildLabelValue("税抜金額", bucket.TaxableAmount, fontSize);
        Grid.SetColumn(taxable, 1);
        row.Children.Add(taxable);

        var tax = BuildLabelValue("消費税", bucket.TaxAmount, fontSize);
        Grid.SetColumn(tax, 3);
        row.Children.Add(tax);

        return row;
    }

    protected static FrameworkElement BuildLabelValue(string label, decimal value, double fontSize = 9)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Tb($"{label}:", fontSize));
        panel.Children.Add(new Rectangle { Width = 4, Fill = Brushes.Transparent });
        var valueText = Tb(value.ToString("N0"), fontSize, FontWeights.Bold, TextAlignment.Right);
        valueText.MinWidth = 80;
        panel.Children.Add(valueText);
        return panel;
    }

    protected static FrameworkElement BuildTotalRow(string label, decimal value, bool large, double scale = 1.0)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        var labelText = Tb(
            label, (large ? 10.0 : 9.0) * scale, large ? FontWeights.Bold : FontWeights.Normal);
        labelText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(labelText, 0);
        grid.Children.Add(labelText);

        var valueText = Tb(
            value.ToString("N0"), (large ? 14.0 : 10.0) * scale,
            large ? FontWeights.Bold : FontWeights.Normal, TextAlignment.Right);
        valueText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);

        return grid;
    }
}
