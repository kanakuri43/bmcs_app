using bmcs_app.Domain.Enums;
using bmcs_app.Domain.Numbering;

namespace bmcs_app.Domain.Tests.Numbering;

public class SlipNumberFormatterTests
{
    [Theory]
    [InlineData(1, "00000001")]
    [InlineData(107, "00000107")]
    [InlineData(99999999, "99999999")]
    [InlineData(100000000, "100000000")] // 8桁を超えても varchar(20) の範囲内で自然に桁が伸びる
    public void 現在値を8桁ゼロ埋めの文字列にする(long sequenceValue, string expected)
    {
        var actual = SlipNumberFormatter.Format(sequenceValue);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(SlipNumberKind.OrderSlip, "order_slip")]
    [InlineData(SlipNumberKind.SalesSlip, "sales_slip")]
    [InlineData(SlipNumberKind.ReceiptSlip, "receipt_slip")]
    [InlineData(SlipNumberKind.DetailReceipt, "detail_receipt")]
    [InlineData(SlipNumberKind.Billing, "billing")]
    [InlineData(SlipNumberKind.DetailInvoice, "detail_invoice")]
    public void 採番系列をslip_number_sequenceの実キーに解決する(SlipNumberKind kind, string expectedSequenceKey)
    {
        var actual = SlipNumberFormatter.ToSequenceKey(kind);
        Assert.Equal(expectedSequenceKey, actual);
    }
}
