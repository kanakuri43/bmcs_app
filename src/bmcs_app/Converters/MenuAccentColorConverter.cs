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
            Color.FromRgb(0x90, 0x66, 0xFF), // パープル
            Color.FromRgb(0x5F, 0x97, 0xF6), // ブルー
            Color.FromRgb(0x26, 0xC3, 0xB6), // ターコイズ
            Color.FromRgb(0xFF, 0xA0, 0x5B), // オレンジ
            Color.FromRgb(0xF2, 0x73, 0x94), // コーラルピンク
            Color.FromRgb(0x5F, 0xC3, 0x94), // ミントグリーン
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
