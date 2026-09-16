using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace bmcs_app.Behaviors;

/// <summary>
/// IME オン時に SPACE キーが Key.ImeProcessed として届くため、TextBox.InputBindings の
/// &lt;KeyBinding Key="Space"/&gt;（コード欄のSPACE検索）が反応しない問題への対応。
/// ImeProcessedKey が Space の場合も、フォーカス中の要素が持つ Space の KeyBinding を実行する。
/// </summary>
public static class ImeAwareSpaceKeyBindingBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ImeAwareSpaceKeyBindingBehavior),
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
        if (e.Key != Key.ImeProcessed || e.ImeProcessedKey != Key.Space)
        {
            return;
        }

        if (e.OriginalSource is not FrameworkElement element)
        {
            return;
        }

        var binding = element.InputBindings.OfType<KeyBinding>()
            .FirstOrDefault(kb => kb.Key == Key.Space && kb.Modifiers == Keyboard.Modifiers);

        if (binding?.Command is not { } command || !command.CanExecute(binding.CommandParameter))
        {
            return;
        }

        command.Execute(binding.CommandParameter);
        e.Handled = true;
    }
}
