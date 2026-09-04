using System.Globalization;
using System.Windows.Data;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Converters;

/// <summary>TaxUnit/RoundingType を画面表示用の日本語に変換する。</summary>
public class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TaxUnit.Invoice => "外税一括",
        TaxUnit.Slip => "外税伝票単位",
        TaxUnit.Line => "内税明細単位",
        RoundingType.Floor => "切捨",
        RoundingType.RoundHalfUp => "四捨五入",
        RoundingType.Ceiling => "切上",
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
