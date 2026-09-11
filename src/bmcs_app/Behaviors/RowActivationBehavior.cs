using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace bmcs_app.Behaviors;

/// <summary>
/// 検索モーダル・明細一覧向けの共通ビヘイビア（docs/product-spec.md UI/UX節:
/// 「検索モーダル・明細一覧は上下矢印キーで行選択、Enter で確定・転記」
/// 「リストからの選択は Enter キーによる確定を前提とするが、ダブルクリックでも同じ動作をする」）。
/// 上下矢印による行選択は Selector（ListBox/ListView/DataGrid 等）の既定動作に委ね、
/// このビヘイビアは Enter キー／行のダブルクリックで選択中の行をコマンドへ渡す配線のみを担う。
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
        selector.PreviewMouseDoubleClick -= OnPreviewMouseDoubleClick;

        if (e.NewValue is not null)
        {
            selector.PreviewKeyDown += OnPreviewKeyDown;
            selector.PreviewMouseDoubleClick += OnPreviewMouseDoubleClick;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var selector = (Selector)sender;

        if (e.Key != Key.Enter || selector.SelectedItem is null)
        {
            return;
        }

        e.Handled = Activate(selector, selector.SelectedItem);
    }

    private static void OnPreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var selector = (Selector)sender;

        // ヘッダやスクロールバー、一覧下の空白領域のダブルクリックでは何も確定しない。
        // クリックされた実際の行のコンテナを解決し、そこに紐づくデータ項目だけを対象にする。
        if (ItemsControl.ContainerFromElement(selector, (DependencyObject)e.OriginalSource) is not FrameworkElement container)
        {
            return;
        }

        var item = selector.ItemContainerGenerator.ItemFromContainer(container);
        if (item is null || item == DependencyProperty.UnsetValue)
        {
            return;
        }

        e.Handled = Activate(selector, item);
    }

    /// <summary>Enter キー・ダブルクリックの両方から呼ぶ、確定処理の唯一の実装。</summary>
    private static bool Activate(Selector selector, object item)
    {
        var command = GetCommand(selector);
        if (command is null || !command.CanExecute(item))
        {
            return false;
        }

        command.Execute(item);
        return true;
    }
}
