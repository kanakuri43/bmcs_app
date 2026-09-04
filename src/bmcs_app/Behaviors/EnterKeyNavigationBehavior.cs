using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace bmcs_app.Behaviors;

/// <summary>
/// Enter キーを Tab と同様に次項目へのフォーカス移動として扱う添付ビヘイビア
/// （docs/product-spec.md UI/UX節: 「Tab だけでなく Enter でも次項目へフォーカス移動」）。
/// TextBox 以外（Button 等）のフォーカス中は反応せず、既定の Enter 動作を妨げない。
/// </summary>
public static class EnterKeyNavigationBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(EnterKeyNavigationBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.PreviewKeyDown -= OnPreviewKeyDown;

        if ((bool)e.NewValue)
        {
            element.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is not TextBox textBox)
        {
            return;
        }

        textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
    }
}
