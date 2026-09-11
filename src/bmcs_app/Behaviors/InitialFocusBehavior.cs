using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using bmcs_app.ViewModels;

namespace bmcs_app.Behaviors;

/// <summary>
/// 画面の先頭入力項目向けの共通ビヘイビア（docs/product-spec.md UI/UX節:
/// 「起動直後のフォーカス」「登録後のリセット」）。
/// 付けたコントロールへ、ウィンドウ表示直後と DataContext（<see cref="ViewModelBase"/>）の
/// <see cref="ViewModelBase.ResetToInitialState"/> 発火時にキーボードフォーカスを当てる。
/// DataContext はウィンドウ生成時に設定済み（<see cref="Services.WindowService"/> が
/// Show/ShowDialog 前に設定する）ため、Loaded 時点で参照できる前提とする。
/// </summary>
public static class InitialFocusBehavior
{
    public static readonly DependencyProperty IsInitialFocusProperty = DependencyProperty.RegisterAttached(
        "IsInitialFocus",
        typeof(bool),
        typeof(InitialFocusBehavior),
        new PropertyMetadata(false, OnIsInitialFocusChanged));

    public static bool GetIsInitialFocus(DependencyObject obj) => (bool)obj.GetValue(IsInitialFocusProperty);

    public static void SetIsInitialFocus(DependencyObject obj, bool value) => obj.SetValue(IsInitialFocusProperty, value);

    private static void OnIsInitialFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.Loaded -= OnLoaded;

        if ((bool)e.NewValue)
        {
            element.Loaded += OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
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

    private static void RequestFocus(FrameworkElement element)
    {
        // 読み込み・レイアウト直後は既定のフォーカス処理がまだ残っていることがあるため、
        // それらが一巡した後（Input優先度）でフォーカスを当てる。
        element.Dispatcher.BeginInvoke(() => Keyboard.Focus(element), DispatcherPriority.Input);
    }
}
