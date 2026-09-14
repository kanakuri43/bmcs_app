using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

public class AllocationStatusCalculatorTests
{
    [Theory]
    [InlineData(11000, 0, AllocationStatus.Unallocated)]
    [InlineData(11000, 4000, AllocationStatus.PartiallyAllocated)]
    [InlineData(11000, 11000, AllocationStatus.FullyAllocated)]
    public void 入金額と充当額合計から状態を判定する(double receiptAmount, double allocatedTotal, AllocationStatus expected)
    {
        var actual = AllocationStatusCalculator.Determine((decimal)receiptAmount, (decimal)allocatedTotal);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 充当額がゼロなら前受として未充当になる()
    {
        var actual = AllocationStatusCalculator.Determine(3000m, 0m);
        Assert.Equal(AllocationStatus.Unallocated, actual);
    }

    [Fact]
    public void 充当額が入金額を超えても充当完了になる()
    {
        var actual = AllocationStatusCalculator.Determine(10000m, 10500m);
        Assert.Equal(AllocationStatus.FullyAllocated, actual);
    }

    [Fact]
    public void マイナス入金額は絶対値で判定する()
    {
        // 現行の業務ルールでは入金額がマイナスになることは想定していないが、
        // 符号対称な防御的挙動として確認する。
        var actual = AllocationStatusCalculator.Determine(-5000m, -5000m);
        Assert.Equal(AllocationStatus.FullyAllocated, actual);
    }
}
