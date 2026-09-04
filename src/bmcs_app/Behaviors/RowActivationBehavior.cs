using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace bmcs_app.Behaviors;

/// <summary>
/// 検索モーダル・明細一覧向けの共通ビヘイビア（docs/product-spec.md UI/UX節:
/// 「検索モーダル・明細一覧は上下矢印キーで行選択、Enter で確定・転記」）。
/// 上下矢印による行選択は Selector（ListBox/ListView/DataGrid 等）の既定動作に委ね、
/// このビヘイビアは Enter キーで選択中の行をコマンドへ渡す配線のみを担う。
/// 「転記」の具体的な処理は呼び出し側画面のコマンド実装に委ねる。
/// </summary>
public static class RowActivationBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(RowActivationBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject obj) => (ICommand?)obj.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject obj, ICommand? value) => obj.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Selector selector)
        {
            return;
        }

        selector.PreviewKeyDown -= OnPreviewKeyDown;

        if (e.NewValue is not null)
        {
            selector.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var selector = (Selector)sender;

        if (e.Key != Key.Enter || selector.SelectedItem is null)
        {
            return;
        }

        var command = GetCommand(selector);
        if (command is null || !command.CanExecute(selector.SelectedItem))
        {
            return;
        }

        command.Execute(selector.SelectedItem);
        e.Handled = true;
    }
}
