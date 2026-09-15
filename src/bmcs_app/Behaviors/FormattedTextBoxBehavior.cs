using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace bmcs_app.Behaviors;

/// <summary>
/// 金額・数量の表示書式（docs/product-spec.md UI/UX節）を
/// TextBox に対して共通に適用する添付ビヘイビア。
/// フォーカス時はカンマなしの生数値、blur 時はカンマ区切りに整形する。
/// 値の妥当性検証は行わない（形式チェックは ViewModel の責務。docs/architecture.md 11章）。
/// 日付は<see cref="DatePickerInputBehavior"/>（DatePicker専用）へ移行済み（全画面デザイン統一・2026-09-15）。
/// </summary>
public static class FormattedTextBoxBehavior
{
    public enum FormattedTextBoxKind
    {
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
        textBox.PreviewKeyDown -= OnPreviewKeyDown;

        if (e.NewValue is FormattedTextBoxKind)
        {
            textBox.Loaded += OnLoaded;
            textBox.GotFocus += OnGotFocus;
            textBox.LostFocus += OnLostFocus;
            textBox.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    /// <summary>
    /// Enter キーで、フォーカスを外さなくても入力値をすぐに確定する。
    /// <c>UpdateSourceTrigger=LostFocus</c>のバインディングはフォーカスが外れるまで
    /// ViewModel 側の値が更新されないため、値を入力した直後に Enter を押すだけでは
    /// 何も反映されない（画面によっては自動再取得の起点にもならない）。これは
    /// 「値を入力したら Enter で確定する」という、本アプリの他の入力欄（請求番号等の
    /// コード直接入力）と同じ操作感をユーザーが期待するため、直感に反する
    /// （2026-09-15、締め解除処理〈請求日入力〉で実機確認した不具合）。
    /// ※当該の日付欄はその後 DatePicker 化した（<see cref="DatePickerInputBehavior"/>）ため、
    /// 現在この処理の適用対象は金額・数量欄のみ。
    /// </summary>
    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        var textBox = (TextBox)sender;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
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

    private static void Reformat(TextBox textBox)
    {
        var kind = GetKind(textBox);

        switch (kind)
        {
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
