using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Tests.Calculations;

public class OrderStatusCalculatorTests
{
    [Theory]
    [InlineData(10, 0, OrderStatus.NotSold)]
    [InlineData(10, 3, OrderStatus.PartiallySold)]
    [InlineData(10, 9.999, OrderStatus.PartiallySold)]
    [InlineData(10, 10, OrderStatus.FullySold)]
    public void 受注数量と売上化済数量から状態を判定する(double orderQuantity, double salesConfirmedQuantity, OrderStatus expected)
    {
        var actual = OrderStatusCalculator.Determine((decimal)orderQuantity, (decimal)salesConfirmedQuantity);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 受注数量ゼロで売上化済数量もゼロなら未売上()
    {
        var actual = OrderStatusCalculator.Determine(0m, 0m);
        Assert.Equal(OrderStatus.NotSold, actual);
    }

    [Fact]
    public void 売上化済数量が受注数量を超えても売上完了扱い()
    {
        // 呼び出し元（OrderStatusService）が超過を拒否するため、判定自体は超過値を防御しない。
        var actual = OrderStatusCalculator.Determine(10m, 12m);
        Assert.Equal(OrderStatus.FullySold, actual);
    }

    [Fact]
    public void 負の売上化済数量は未売上扱い()
    {
        var actual = OrderStatusCalculator.Determine(10m, -1m);
        Assert.Equal(OrderStatus.NotSold, actual);
    }
}
