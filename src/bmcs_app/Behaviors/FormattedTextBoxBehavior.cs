using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace bmcs_app.Behaviors;

/// <summary>
/// 日付・金額・数量の表示書式（docs/product-spec.md UI/UX節）を
/// TextBox に対して共通に適用する添付ビヘイビア。
/// フォーカス時はカンマなしの生数値、blur 時はカンマ区切り・yyyy/MM/dd 等に整形する。
/// 値の妥当性検証は行わない（形式チェックは ViewModel の責務。docs/architecture.md 11章）。
/// </summary>
public static class FormattedTextBoxBehavior
{
    public enum FormattedTextBoxKind
    {
        Date,
        Amount,
        Quantity,
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind",
        typeof(FormattedTextBoxKind?),
        typeof(FormattedTextBoxBehavior),
        new PropertyMetadata(null, OnKindChanged));

    public static readonly DependencyProperty DecimalPlacesProperty = DependencyProperty.RegisterAttached(
        "DecimalPlaces",
        typeof(int),
        typeof(FormattedTextBoxBehavior),
        new PropertyMetadata(0));

    public static FormattedTextBoxKind? GetKind(DependencyObject obj) => (FormattedTextBoxKind?)obj.GetValue(KindProperty);

    public static void SetKind(DependencyObject obj, FormattedTextBoxKind? value) => obj.SetValue(KindProperty, value);

    public static int GetDecimalPlaces(DependencyObject obj) => (int)obj.GetValue(DecimalPlacesProperty);

    public static void SetDecimalPlaces(DependencyObject obj, int value) => obj.SetValue(DecimalPlacesProperty, value);

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
        {
            return;
        }

        textBox.Loaded -= OnLoaded;
        textBox.GotFocus -= OnGotFocus;
        textBox.LostFocus -= OnLostFocus;

        if (e.NewValue is FormattedTextBoxKind)
        {
            textBox.Loaded += OnLoaded;
            textBox.GotFocus += OnGotFocus;
            textBox.LostFocus += OnLostFocus;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Reformat((TextBox)sender);

    private static void OnLostFocus(object sender, RoutedEventArgs e)
    {
        var textBox = (TextBox)sender;

        // バインディングの UpdateSourceTrigger=LostFocus による値の反映は、
        // この LostFocus イベントを介して行われる（実行順序は保証されない）。
        // Text の書式変更を Dispatcher で後回しにし、値の反映が必ず先に
        // 完了してから表示を整形する（生の数値のままソースへ反映させるため）。
        textBox.Dispatcher.BeginInvoke(() => Reformat(textBox), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void OnGotFocus(object sender, RoutedEventArgs e)
    {
        var textBox = (TextBox)sender;
        var kind = GetKind(textBox);

        // 日付はフォーカス時もカンマ整形の対象外なので書式を変えない。
        if (kind != FormattedTextBoxKind.Amount && kind != FormattedTextBoxKind.Quantity)
        {
            return;
        }

        // マウスクリックでフォーカスした場合、WPF はこの GotFocus 処理の後に
        // クリック位置へキャレットを移動する処理を行うため、ここで同期的に
        // Text の書き換えや SelectAll を行うと直後に上書きされる。
        // Dispatcher で後回しにし、クリックのキャレット処理が終わってから実行する。
        textBox.Dispatcher.BeginInvoke(() =>
        {
            if (!decimal.TryParse(textBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value))
            {
                return;
            }

            var decimalPlaces = GetDecimalPlaces(textBox);
            textBox.Text = value.ToString("F" + decimalPlaces, CultureInfo.CurrentCulture);
            textBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    // 表示書式（yyyy/MM/dd）に加えて、区切りなしの入力（例: 20260101）も許容する。
    private static readonly string[] DateInputFormats = ["yyyy/MM/dd", "yyyy/M/d", "yyyy-MM-dd", "yyyyMMdd"];

    private static void Reformat(TextBox textBox)
    {
        var kind = GetKind(textBox);

        switch (kind)
        {
            case FormattedTextBoxKind.Date:
                if (DateOnly.TryParseExact(textBox.Text, DateInputFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    || DateOnly.TryParse(textBox.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
                {
                    textBox.Text = date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
                }

                break;

            case FormattedTextBoxKind.Amount:
            case FormattedTextBoxKind.Quantity:
                if (decimal.TryParse(textBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value))
                {
                    var decimalPlaces = GetDecimalPlaces(textBox);
                    textBox.Text = value.ToString("N" + decimalPlaces, CultureInfo.CurrentCulture);
                }

                break;
        }
    }
}
