using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace bmcs_app.Payment.Views;

public partial class MasterSearchDialog : MahApps.Metro.Controls.MetroWindow
{
    public record SearchItem(string Code, string Name, object Source);

    private readonly List<SearchItem> _allItems;

    public SearchItem? SelectedSearchItem { get; private set; }

    public MasterSearchDialog(string title, IEnumerable<SearchItem> items, string initialKeyword = "")
    {
        InitializeComponent();
        Title     = title;
        _allItems = items.ToList();

        KeywordBox.Text = initialKeyword;
        ApplyFilter(initialKeyword);

        Loaded += (_, _) =>
        {
            KeywordBox.Focus();
            KeywordBox.SelectAll();
        };
    }

    private void ApplyFilter(string keyword)
    {
        var filtered = string.IsNullOrWhiteSpace(keyword)
            ? _allItems
            : _allItems.Where(i =>
                i.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                i.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
              .ToList();

        ResultGrid.ItemsSource = filtered;

        if (filtered.Count > 0)
            ResultGrid.SelectedIndex = 0;
    }

    private void Confirm()
    {
        if (ResultGrid.SelectedItem is SearchItem item)
        {
            SelectedSearchItem = item;
            DialogResult = true;
        }
    }

    private void KeywordBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter(KeywordBox.Text);

    private void KeywordBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                ResultGrid.Focus();
                if (ResultGrid.Items.Count > 0)
                    ResultGrid.SelectedIndex = 0;
                e.Handled = true;
                break;
            case Key.Return:
                Confirm();
                e.Handled = true;
                break;
        }
    }

    private void ResultGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src &&
            ItemsControl.ContainerFromElement(ResultGrid, src) is DataGridRow)
            Confirm();
    }

    private void ResultGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            Confirm();
            e.Handled = true;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => Confirm();
    private void CancelButton_Click(object sender, RoutedEventArgs e)  => DialogResult = false;
}
