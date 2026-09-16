using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace bmcs_app.Behaviors;

/// <summary>
/// Enter キーを Tab と同様に次項目へのフォーカス移動として扱う添付ビヘイビア
/// （docs/product-spec.md UI/UX節: 「Tab だけでなく Enter でも次項目へフォーカス移動」）。
/// 対象は TextBox と（ドロップダウンを閉じている）ComboBox のみ。Button 等のフォーカス中は
/// 反応せず、既定の Enter 動作を妨げない。
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
        if (e.Key != Key.Enter)
        {
            return;
        }

        switch (e.OriginalSource)
        {
            case TextBox textBox:
                // その TextBox 自身が Enter に対する KeyBinding を持つ場合はここで奪わない
                // （商品コード欄の「Enterでコード確定」等、個別の挙動を上書きしてしまうため）。
                // PreviewKeyDown はルートから対象へトンネリングするため、この添付ビヘイビアを
                // 付けた祖先コンテナのハンドラは、TextBox 自身の InputBindings 処理より先に走る。
                var hasOwnEnterBinding = textBox.InputBindings.OfType<KeyBinding>()
                    .Any(kb => kb.Key == Key.Enter && kb.Modifiers == Keyboard.Modifiers);
                if (hasOwnEnterBinding)
                {
                    return;
                }

                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
                break;

            // ドロップダウンを開いたまま Enter を押した場合は、既定動作（選択確定して閉じる）を
            // 優先する。閉じた状態（矢印キーだけで選択する一般的な操作）で Enter を押したときだけ
            // 次項目へ移動する。
            case ComboBox { IsDropDownOpen: false } comboBox:
                comboBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
                break;
        }
    }
}
