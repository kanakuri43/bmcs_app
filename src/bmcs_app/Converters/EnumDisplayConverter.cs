using System.Globalization;
using System.Windows.Data;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Converters;

/// <summary>TaxUnit/RoundingType/TaxCategory を画面表示用の日本語に変換する。</summary>
public class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TaxUnit.Invoice => "請求単位",
        TaxUnit.Slip => "伝票単位",
        TaxUnit.Line => "内税明細単位",
        RoundingType.Floor => "切捨",
        RoundingType.RoundHalfUp => "四捨五入",
        RoundingType.Ceiling => "切上",
        TaxCategory.Standard => "課税10%",
        TaxCategory.Reduced => "軽減8%",
        TaxCategory.TaxExempt => "非課税",
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
