using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace bmcs_app.Converters;

/// <summary>
/// 数値がマイナスのとき赤、それ以外は既定色（UnsetValue）にする、Foreground用の一方向コンバータ。
/// </summary>
public class NegativeToRedBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d && d < 0m ? Brushes.Red : DependencyProperty.UnsetValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
