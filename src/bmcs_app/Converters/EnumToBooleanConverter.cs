using System.Globalization;
using System.Windows.Data;

namespace bmcs_app.Converters;

/// <summary>
/// enum値をRadioButtonのIsCheckedにバインドするための汎用コンバータ。
/// ConverterParameterに対象enumのメンバー名（文字列）を指定する。
/// </summary>
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is string parameterString && value.ToString() == parameterString;

    public object? ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is string parameterString
            ? Enum.Parse(targetType, parameterString)
            : Binding.DoNothing;
}
