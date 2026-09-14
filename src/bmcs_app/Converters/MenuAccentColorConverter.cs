using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace bmcs_app.Converters;

/// <summary>
/// メインメニュー（TODO.md 2-7）のカテゴリ見出しの色分けに使う。ViewModel は色を持たず、
/// カテゴリの表示順に応じた整数インデックスだけを持つ（ViewModel が WPF の Media 型に依存しないため）。
/// </summary>
public class MenuAccentColorConverter : IValueConverter
{
    private static readonly Brush[] Palette = BuildPalette();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int index || Palette.Length == 0)
        {
            return Brushes.Gray;
        }

        return Palette[((index % Palette.Length) + Palette.Length) % Palette.Length];
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush[] BuildPalette()
    {
        var colors = new[]
        {
            Color.FromRgb(0x1E, 0x88, 0xE5), // 青
            Color.FromRgb(0x00, 0x89, 0x7B), // 青緑
            Color.FromRgb(0x60, 0x7D, 0x8B), // 藍鼠
            Color.FromRgb(0x43, 0xA0, 0x47), // 緑
            Color.FromRgb(0x7E, 0x57, 0xC2), // 紫
            Color.FromRgb(0xFB, 0x8C, 0x00), // 橙
        };

        var brushes = new Brush[colors.Length];
        for (var i = 0; i < colors.Length; i++)
        {
            var brush = new SolidColorBrush(colors[i]);
            brush.Freeze();
            brushes[i] = brush;
        }

        return brushes;
    }
}
