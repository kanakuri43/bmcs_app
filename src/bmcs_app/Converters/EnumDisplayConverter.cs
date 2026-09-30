using System.Globalization;
using System.Windows.Data;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Converters;

/// <summary>
/// TaxUnit/RoundingType/TaxCategory/OrderStatus/BillingLinkStatus/SettlementStatus/SlipType/BillingStatus/
/// DetailInvoiceStatus/BankAccountType/AllocationStatus/LedgerEntryKind
/// を画面表示用の日本語に変換する。
/// </summary>
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
        TaxCategory.Standard => "通常税率",
        TaxCategory.Reduced => "軽減税率",
        TaxCategory.TaxExempt => "非課税",
        OrderStatus.NotSold => "未売上",
        OrderStatus.PartiallySold => "一部売上",
        OrderStatus.FullySold => "売上完了",
        OrderStatus.Cancelled => "中止",
        BillingLinkStatus.Unbilled => "未請求",
        BillingLinkStatus.Billed => "請求済",
        SettlementStatus.Unsettled => "未消込",
        SettlementStatus.PartiallySettled => "一部消込",
        SettlementStatus.FullySettled => "消込完了",
        SlipType.Sales => "売上",
        SlipType.Return => "返品",
        SlipType.Discount => "値引",
        BillingStatus.Confirmed => "確定",
        BillingStatus.Released => "解除済",
        DetailInvoiceStatus.Issued => "発行済",
        DetailInvoiceStatus.Cancelled => "取消済",
        BankAccountType.Ordinary => "普通",
        BankAccountType.Checking => "当座",
        AllocationStatus.Unallocated => "未充当",
        AllocationStatus.PartiallyAllocated => "一部充当",
        AllocationStatus.FullyAllocated => "充当完了",
        LedgerEntryKind.OpeningBalance => "前月繰越",
        LedgerEntryKind.Sales => "売上",
        LedgerEntryKind.ConsumptionTax => "消費税",
        LedgerEntryKind.Receipt => "入金",
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
