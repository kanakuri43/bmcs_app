using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace bmcs_app.Behaviors;

/// <summary>
/// ListBox/ListView（<c>SelectionMode="Extended"</c>）の複数選択結果を ViewModel の
/// コレクションへ同期する（TODO.md 10-7、請求締め処理画面の複数選択印刷で導入）。
/// <see cref="ListBox.SelectedItems"/>は依存関係プロパティでないため直接バインドできず、
/// このビヘイビアで View→VM の一方向同期を行う（VM側から選択状態を書き換えるユースケースは無い）。
/// </summary>
public static class MultiSelectionBehavior
{
    public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.RegisterAttached(
        "SelectedItems",
        typeof(IList),
        typeof(MultiSelectionBehavior),
        new PropertyMetadata(null, OnSelectedItemsChanged));

    public static IList? GetSelectedItems(DependencyObject obj) => (IList?)obj.GetValue(SelectedItemsProperty);

    public static void SetSelectedItems(DependencyObject obj, IList? value) => obj.SetValue(SelectedItemsProperty, value);

    private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox)
        {
            return;
        }

        listBox.SelectionChanged -= OnSelectionChanged;

        if (e.NewValue is not null)
        {
            listBox.SelectionChanged += OnSelectionChanged;
        }
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var listBox = (ListBox)sender;
        if (GetSelectedItems(listBox) is not IList target)
        {
            return;
        }

        target.Clear();
        foreach (var item in listBox.SelectedItems)
        {
            target.Add(item);
        }
    }
}
