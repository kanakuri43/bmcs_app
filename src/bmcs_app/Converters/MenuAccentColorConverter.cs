using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace bmcs_app.Converters;

/// <summary>
/// メインメニューのカテゴリ見出しの色分けに使う。ViewModel は色を持たず、
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
            Color.FromRgb(0xE0, 0x59, 0x7C), // 1. コーラルレッド
            Color.FromRgb(0xE0, 0x6C, 0x53), // 2. サーモン
            Color.FromRgb(0xDC, 0x7E, 0x3E), // 3. オレンジ
            Color.FromRgb(0xB7, 0x99, 0x36), // 4. ゴールド
            Color.FromRgb(0x87, 0xA4, 0x4A), // 5. ライムグリーン
            Color.FromRgb(0x4F, 0xA1, 0x78), // 6. グリーン
            Color.FromRgb(0x28, 0xA0, 0x96), // 7. ターコイズ
            Color.FromRgb(0x34, 0x97, 0xB4), // 8. シアンブルー
            Color.FromRgb(0x53, 0x83, 0xDE), // 9. ブルー
            Color.FromRgb(0x84, 0x5E, 0xEA), // 10. パープル
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
