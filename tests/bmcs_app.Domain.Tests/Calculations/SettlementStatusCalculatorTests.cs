using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

public class SettlementStatusCalculatorTests
{
    [Theory]
    [InlineData(10000, 0, SettlementStatus.Unsettled)]
    [InlineData(10000, 5000, SettlementStatus.PartiallySettled)]
    [InlineData(10000, 10000, SettlementStatus.FullySettled)]
    [InlineData(10000, 12000, SettlementStatus.FullySettled)] // 過入金でも消込完了（超過分は入金側に残る）
    public void 売上金額と消込済金額から状態を判定する(double amount, double settledAmount, SettlementStatus expected)
    {
        var actual = SettlementStatusCalculator.Determine((decimal)amount, (decimal)settledAmount);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 売上金額がゼロの行は常に未消込()
    {
        var actual = SettlementStatusCalculator.Determine(0m, 0m);
        Assert.Equal(SettlementStatus.Unsettled, actual);
    }

    [Theory]
    [InlineData(-1100, 0, SettlementStatus.Unsettled)]
    [InlineData(-1100, -500, SettlementStatus.PartiallySettled)]
    [InlineData(-1100, -1100, SettlementStatus.FullySettled)]
    public void マイナス金額の返品行は絶対値で判定する(double amount, double settledAmount, SettlementStatus expected)
    {
        var actual = SettlementStatusCalculator.Determine((decimal)amount, (decimal)settledAmount);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 売上金額と符号が逆の消込済金額は一部消込として扱う()
    {
        // 呼び出し元（SettlementAllocator/SettlementService）は本来この組み合わせを作らないが、
        // 純粋関数として符号不一致を「満額ではない」側に落とす防御的な挙動を確認する。
        var actual = SettlementStatusCalculator.Determine(10000m, -3000m);
        Assert.Equal(SettlementStatus.PartiallySettled, actual);
    }
}
