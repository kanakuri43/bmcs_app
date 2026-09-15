using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using bmcs_app.ViewModels;

namespace bmcs_app.Behaviors;

/// <summary>
/// 入力欄への自動フォーカス投入を扱う共通ビヘイビア（docs/product-spec.md UI/UX節:
/// 「起動直後のフォーカス」「登録後のリセット」「ジャーナル系画面の伝票No入力欄の挙動」）。
/// DataContext はウィンドウ生成時に設定済み（<see cref="Services.WindowService"/> が
/// Show/ShowDialog 前に設定する）ため、Loaded 時点で参照できる前提とする。
/// </summary>
public static class FocusBehavior
{
    /// <summary>
    /// 画面の先頭入力項目に付ける。ウィンドウ表示直後と <see cref="ViewModelBase.ResetToInitialState"/>
    /// 発火時にキーボードフォーカスを当てる。
    /// </summary>
    public static readonly DependencyProperty IsInitialFocusProperty = DependencyProperty.RegisterAttached(
        "IsInitialFocus",
        typeof(bool),
        typeof(FocusBehavior),
        new PropertyMetadata(false, OnIsInitialFocusChanged));

    public static bool GetIsInitialFocus(DependencyObject obj) => (bool)obj.GetValue(IsInitialFocusProperty);

    public static void SetIsInitialFocus(DependencyObject obj, bool value) => obj.SetValue(IsInitialFocusProperty, value);

    /// <summary>
    /// 伝票No欄の呼び出し結果（空欄Enter＝次項目／読込成功）に応じてフォーカスを受け取る入力欄に付ける。
    /// <see cref="ViewModelBase.FocusRequested"/> が発火したキーと一致する要素へフォーカスを当てる。
    /// </summary>
    public static readonly DependencyProperty FocusKeyProperty = DependencyProperty.RegisterAttached(
        "FocusKey",
        typeof(string),
        typeof(FocusBehavior),
        new PropertyMetadata(null, OnFocusKeyChanged));

    public static string? GetFocusKey(DependencyObject obj) => (string?)obj.GetValue(FocusKeyProperty);

    public static void SetFocusKey(DependencyObject obj, string? value) => obj.SetValue(FocusKeyProperty, value);

    private static void OnIsInitialFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.Loaded -= OnInitialFocusLoaded;

        if ((bool)e.NewValue)
        {
            element.Loaded += OnInitialFocusLoaded;
        }
    }

    private static void OnInitialFocusLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;

        if (element.DataContext is ViewModelBase viewModel)
        {
            void OnResetToInitialState(object? _, EventArgs __) => RequestFocus(element);

            viewModel.ResetToInitialState += OnResetToInitialState;

            // ウィンドウを閉じたときに確実に購読解除する（スコープが破棄されても
            // イベント購読だけが残ると ViewModel が解放されなくなるため）。
            element.Unloaded += (_, _) => viewModel.ResetToInitialState -= OnResetToInitialState;
        }

        RequestFocus(element);
    }

    private static void OnFocusKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.Loaded -= OnFocusKeyLoaded;

        if (e.NewValue is string { Length: > 0 })
        {
            element.Loaded += OnFocusKeyLoaded;
        }
    }

    private static void OnFocusKeyLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;

        if (element.DataContext is not ViewModelBase viewModel)
        {
            return;
        }

        void OnFocusRequested(object? _, string key)
        {
            if (key == GetFocusKey(element))
            {
                RequestFocus(element);
            }
        }

        viewModel.FocusRequested += OnFocusRequested;
        element.Unloaded += (_, _) => viewModel.FocusRequested -= OnFocusRequested;
    }

    private static void RequestFocus(FrameworkElement element)
    {
        // 読み込み・レイアウト直後、あるいはダイアログを閉じた直後は既定のフォーカス処理が
        // まだ残っていることがあるため、それらが一巡した後（Input優先度）でフォーカスを当てる。
        element.Dispatcher.BeginInvoke(() => Keyboard.Focus(ResolveFocusTarget(element)), DispatcherPriority.Input);
    }

    /// <summary>
    /// DatePicker は Keyboard.Focus(datePicker) を呼んでも本体にフォーカスが当たるだけで、
    /// 内部の PART_TextBox にキャレットが入らない（全画面デザイン統一・2026-09-15）。
    /// Dispatcher で遅延済み（DispatcherPriority.Input）のためテンプレート適用は完了している
    /// 前提で PART_TextBox を解決する。見つからない場合は DatePicker 自身にフォーカスする。
    /// </summary>
    private static FrameworkElement ResolveFocusTarget(FrameworkElement element) =>
        element is DatePicker datePicker && datePicker.Template?.FindName("PART_TextBox", datePicker) is TextBox textBox
            ? textBox
            : element;
}
