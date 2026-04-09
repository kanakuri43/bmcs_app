using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace bmcs_app.Order.Helpers;

/// <summary>
/// Enter キー押下後に次のフォーカス可能要素へ移動する添付プロパティ。
/// KeyBinding のコマンドが先に実行されてから MoveFocus が走るよう
/// Dispatcher.BeginInvoke で一拍置いている。
/// </summary>
public static class FocusHelper
{
    public static readonly DependencyProperty MoveNextOnEnterProperty =
        DependencyProperty.RegisterAttached(
            "MoveNextOnEnter",
            typeof(bool),
            typeof(FocusHelper),
            new PropertyMetadata(false, OnMoveNextOnEnterChanged));

    public static bool GetMoveNextOnEnter(UIElement element)
        => (bool)element.GetValue(MoveNextOnEnterProperty);

    public static void SetMoveNextOnEnter(UIElement element, bool value)
        => element.SetValue(MoveNextOnEnterProperty, value);

    private static readonly KeyEventHandler _onKeyDown = OnKeyDown;

    private static void OnMoveNextOnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        if ((bool)e.NewValue)
            element.AddHandler(UIElement.KeyDownEvent, _onKeyDown, handledEventsToo: true);
        else
            element.RemoveHandler(UIElement.KeyDownEvent, _onKeyDown);
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Return) return;

        var element = (UIElement)sender;
        element.Dispatcher.BeginInvoke(
            () => element.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
            DispatcherPriority.Input);
    }
}
