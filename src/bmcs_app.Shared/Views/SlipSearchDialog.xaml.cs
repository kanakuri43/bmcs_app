using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MahApps.Metro.Controls;

namespace bmcs_app.Shared.Views;

public partial class SlipSearchDialog : MetroWindow
{
    private readonly string[]      _columns;
    private readonly List<string[]> _allRows;
    private readonly int           _keyColumnIndex;   // 確定値として返す列（伝票No.）

    /// <summary>確定した伝票No.（キャンセル時 null）</summary>
    public string? SelectedSlipNo { get; private set; }

    /// <param name="columns">列ヘッダー</param>
    /// <param name="rows">非正規化済みの全行データ（各要素は columns と同数の文字列配列）</param>
    /// <param name="keyColumnIndex">確定時に返す列のインデックス（伝票No. の列）</param>
    /// <param name="initialKeyword">初期キーワード</param>
    public SlipSearchDialog(
        string title,
        string[] columns,
        IEnumerable<string[]> rows,
        int keyColumnIndex = 1,
        string initialKeyword = "")
    {
        InitializeComponent();
        Title           = title;
        _columns        = columns;
        _allRows        = rows.ToList();
        _keyColumnIndex = keyColumnIndex;

        // 列をコードから追加
        foreach (var col in _columns)
        {
            SlipGrid.Columns.Add(new DataGridTextColumn
            {
                Header     = col,
                Binding    = new Binding($"[{col}]"),
                IsReadOnly = true,
            });
        }

        KeywordBox.Text = initialKeyword;
        ApplyFilter(initialKeyword);
        Loaded += (_, _) =>
        {
            KeywordBox.Focus();
            KeywordBox.SelectAll();
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  絞り込み（全列対象）
    // ─────────────────────────────────────────────────────────────

    private void ApplyFilter(string keyword)
    {
        var kw = keyword.Trim();
        IEnumerable<string[]> filtered = string.IsNullOrEmpty(kw)
            ? _allRows
            : _allRows.Where(r => r.Any(cell =>
                cell.Contains(kw, StringComparison.OrdinalIgnoreCase)));

        var table = new DataTable();
        foreach (var col in _columns)
            table.Columns.Add(col);
        foreach (var row in filtered)
            table.Rows.Add(row.Cast<object?>().ToArray());

        SlipGrid.ItemsSource = table.DefaultView;
    }

    private void KeywordBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter(KeywordBox.Text);

    // ─────────────────────────────────────────────────────────────
    //  キーボード操作
    // ─────────────────────────────────────────────────────────────

    private void KeywordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (SlipGrid.Items.Count > 0)
            {
                SlipGrid.SelectedIndex = 0;
                SlipGrid.Focus();
                var row = (DataGridRow?)SlipGrid.ItemContainerGenerator
                    .ContainerFromIndex(0);
                row?.Focus();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Return)
        {
            if (SlipGrid.Items.Count > 0)
                ConfirmAt(0);
            e.Handled = true;
        }
    }

    private void SlipGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && SlipGrid.SelectedIndex >= 0)
        {
            ConfirmAt(SlipGrid.SelectedIndex);
            e.Handled = true;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  確定・キャンセル
    // ─────────────────────────────────────────────────────────────

    private void SlipGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep
            && FindParent<DataGridRow>(dep) is not null
            && SlipGrid.SelectedIndex >= 0)
        {
            ConfirmAt(SlipGrid.SelectedIndex);
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var index = SlipGrid.SelectedIndex >= 0 ? SlipGrid.SelectedIndex : 0;
        if (SlipGrid.Items.Count > 0)
            ConfirmAt(index);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void ConfirmAt(int index)
    {
        if (index < 0 || index >= SlipGrid.Items.Count) return;
        if (SlipGrid.Items[index] is DataRowView row
            && _keyColumnIndex < row.Row.ItemArray.Length)
        {
            SelectedSlipNo = row.Row.ItemArray[_keyColumnIndex]?.ToString();
            DialogResult   = true;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  VisualTree ヘルパー
    // ─────────────────────────────────────────────────────────────

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
        return parent switch
        {
            null => null,
            T p  => p,
            _    => FindParent<T>(parent),
        };
    }
}
