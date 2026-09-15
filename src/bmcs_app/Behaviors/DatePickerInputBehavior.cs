using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace bmcs_app.Behaviors;

/// <summary>
/// DatePicker に対し、8桁ベタ打ち等の入力（例: <c>20260101</c>）を <c>yyyy/MM/dd</c> へ
/// 正規化する添付ビヘイビア（全画面デザイン統一・2026-09-15、TODO.md）。
/// <see cref="FormattedTextBoxBehavior"/> と同じ方針で、書式のみを担当し、
/// 値の妥当性検証は ViewModel の責務とする（docs/architecture.md 11章）。
///
/// DatePicker 内部の PART_TextBox（型は DatePickerTextBox : TextBox）は
/// OnApplyTemplate を伴う Loaded/Template.FindName での取得を要せず、
/// イベントの e.OriginalSource を TextBox にキャストするだけで扱える
/// （購読・解除の管理やテンプレート再適用のタイミング問題を避けるため、
/// あえて PART_TextBox を明示的には取得しない設計にしている）。
/// </summary>
public static class DatePickerInputBehavior
{
    // 区切りなしの入力（例: 20260101）も許容する（FormattedTextBoxBehavior から移設）。
    private static readonly string[] DateInputFormats = ["yyyy/MM/dd", "yyyy/M/d", "yyyy-MM-dd", "yyyyMMdd"];

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(DatePickerInputBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DatePicker datePicker)
        {
            return;
        }

        datePicker.PreviewKeyDown -= OnPreviewKeyDown;
        datePicker.PreviewLostKeyboardFocus -= OnPreviewLostKeyboardFocus;
        datePicker.GotFocus -= OnGotFocus;

        if ((bool)e.NewValue)
        {
            datePicker.PreviewKeyDown += OnPreviewKeyDown;
            datePicker.PreviewLostKeyboardFocus += OnPreviewLostKeyboardFocus;
            datePicker.GotFocus += OnGotFocus;
        }
    }

    /// <summary>
    /// Enter キーで、フォーカスを外さなくても入力値をすぐに確定する
    /// （FormattedTextBoxBehavior.OnPreviewKeyDown と同じ意図。コミット 26d87c5 参照）。
    /// EnterKeyNavigationBehavior を付けていない画面（締め／解除／元帳）では、この経路だけが
    /// 確定手段になる（Enter でフォーカスが移動しないため）。
    /// </summary>
    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (sender is DatePicker datePicker && e.OriginalSource is TextBox textBox)
        {
            Normalize(datePicker, textBox);
        }
    }

    /// <summary>
    /// EnterKeyNavigationBehavior を付けている画面（売上／受注／入金／明細入金／明細請求書）では、
    /// ルート Grid の PreviewKeyDown がトンネリングで先に走り MoveFocus するため、
    /// DatePicker 側の PreviewKeyDown は呼ばれない。フォーカス離脱時にここで確定する。
    /// DatePicker が内部 TextBox の LostFocus を自前で購読しているため、それより確実に
    /// 先に発火する PreviewLostKeyboardFocus を使う（LostFocus だと DatePicker の解析が
    /// 先に走り、8桁ベタ打ちが DateValidationError として食われてしまう）。
    /// </summary>
    private static void OnPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is DatePicker datePicker && e.OriginalSource is TextBox textBox)
        {
            Normalize(datePicker, textBox);
        }
    }

    /// <summary>
    /// 日付欄は初期値（今日／前回値）が入力済みのため、フォーカスが入った瞬間に全選択して
    /// おかないと8桁ベタ打ちが上書きにならず「末尾に継ぎ足す」形になって破綻する。
    /// マウスクリックでフォーカスした場合、WPF はこの GotFocus 処理の後にクリック位置へ
    /// キャレットを移動する処理を行うため、それが終わってから SelectAll する
    /// （FormattedTextBoxBehavior.OnGotFocus と同じ理由）。
    /// </summary>
    private static void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker || e.OriginalSource is not TextBox textBox)
        {
            return;
        }

        textBox.Dispatcher.BeginInvoke(
            textBox.SelectAll,
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private static void Normalize(DatePicker datePicker, TextBox textBox)
    {
        if (string.IsNullOrWhiteSpace(textBox.Text))
        {
            return;
        }

        if (!DateOnly.TryParseExact(textBox.Text, DateInputFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && !DateOnly.TryParse(textBox.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
        {
            // 解釈できない入力は、現在の SelectedDate の表示へ巻き戻す
            // （画面には壊れた文字列、VM には古い日付、という乖離を防ぐ）。
            textBox.Text = datePicker.SelectedDate is { } current
                ? current.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
                : string.Empty;
            return;
        }

        // 時刻成分は必ず 00:00:00 に揃える。混ざると DateTime? の等値比較が崩れ、
        // [ObservableProperty] の「同値なら通知しない」最適化をすり抜けて
        // OnXxxDateChanged（DB再照会）が余計に発火する。
        datePicker.SelectedDate = date.ToDateTime(TimeOnly.MinValue);

        // SelectedDate の代入だけでも DatePicker が表示を書き戻すが、その書式は
        // Language 設定に依存するため、yyyy/MM/dd を明示代入して二重に保証する。
        textBox.Text = date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
    }
}
