using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace bmcs_app.Behaviors;

/// <summary>
/// ListView（GridView）の列見出しクリックでその列の昇順／降順に並べ替える。
/// ListView に <c>IsEnabled="True"</c>、各列に並べ替えるプロパティ名を <c>SortBy</c> で指定する（<c>SortBy</c> 未指定の列は対象外）。
/// 並べ替えは既定のコレクションビューに掛けるため、一覧を取り直しても並び順を保つ。
/// </summary>
public static class GridViewSortBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GridViewSortBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static readonly DependencyProperty SortByProperty = DependencyProperty.RegisterAttached(
        "SortBy", typeof(string), typeof(GridViewSortBehavior), new PropertyMetadata(null));

    public static string? GetSortBy(DependencyObject obj) => (string?)obj.GetValue(SortByProperty);

    public static void SetSortBy(DependencyObject obj, string? value) => obj.SetValue(SortByProperty, value);

    /// <summary>画面を開いたときの並び順にする列の <c>SortBy</c>（降順。未指定なら並べ替えない）。</summary>
    public static readonly DependencyProperty InitialSortDescendingByProperty = DependencyProperty.RegisterAttached(
        "InitialSortDescendingBy", typeof(string), typeof(GridViewSortBehavior), new PropertyMetadata(null));

    public static string? GetInitialSortDescendingBy(DependencyObject obj) => (string?)obj.GetValue(InitialSortDescendingByProperty);

    public static void SetInitialSortDescendingBy(DependencyObject obj, string? value) => obj.SetValue(InitialSortDescendingByProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView listView)
        {
            return;
        }

        listView.Loaded -= OnListViewLoaded;
        listView.RemoveHandler(ButtonBase.ClickEvent, (RoutedEventHandler)OnHeaderClick);
        if ((bool)e.NewValue)
        {
            listView.Loaded += OnListViewLoaded;
            listView.AddHandler(ButtonBase.ClickEvent, (RoutedEventHandler)OnHeaderClick);
        }
    }

    private static void OnListViewLoaded(object sender, RoutedEventArgs e)
    {
        var listView = (ListView)sender;
        if (GetInitialSortDescendingBy(listView) is { } property
            && ((GridView)listView.View).Columns.FirstOrDefault(c => GetSortBy(c) == property) is { } column)
        {
            ApplySort(listView, column, property, ListSortDirection.Descending);
        }
    }

    private static void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ListView listView
            || e.OriginalSource is not GridViewColumnHeader { Column: { } column }
            || GetSortBy(column) is not { } property)
        {
            return;
        }

        var view = CollectionViewSource.GetDefaultView(listView.ItemsSource);
        var direction = view.SortDescriptions is [{ } current] && current.PropertyName == property
                        && current.Direction == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        ApplySort(listView, column, property, direction);
    }

    private static void ApplySort(ListView listView, GridViewColumn column, string property, ListSortDirection direction)
    {
        var view = CollectionViewSource.GetDefaultView(listView.ItemsSource);
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(property, direction));

        // 並べ替え中の列の見出しに ▲（昇順）／▼（降順）を付ける。
        foreach (var other in ((GridView)listView.View).Columns)
        {
            // 見出しは画面側が後から書き換えることがあるため、現在の文字列から矢印だけを外して付け直す。
            if (other.Header is string text)
            {
                var baseHeader = text.TrimEnd(' ', '▲', '▼');
                other.Header = other == column ? baseHeader + (direction == ListSortDirection.Ascending ? " ▲" : " ▼") : baseHeader;
            }
        }
    }
}
