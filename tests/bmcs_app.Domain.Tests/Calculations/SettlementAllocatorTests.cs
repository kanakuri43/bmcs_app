using bmcs_app.Domain.Calculations;

namespace bmcs_app.Domain.Tests.Calculations;

public class SettlementAllocatorTests
{
    [Fact]
    public void 充当額がゼロならどの行も配分されない()
    {
        var actual = SettlementAllocator.Allocate([6000m, 4000m], 0m);
        Assert.Equal([0m, 0m], actual);
    }

    [Fact]
    public void 充当額が対象額の合計以上なら全行が対象額どおりに配分される()
    {
        // 明細6,000円+4,000円=10,000円の請求に対し、消費税込11,000円を入金した例
        // （TODO.md 7-1決定1）。残1,000円（税額分）は行に載せない。
        var actual = SettlementAllocator.Allocate([6000m, 4000m], 11000m);
        Assert.Equal([6000m, 4000m], actual);
    }

    [Fact]
    public void 対象額を超える過入金でも行には対象額までしか配分しない()
    {
        var actual = SettlementAllocator.Allocate([10000m], 15000m);
        Assert.Equal([10000m], actual);
    }

    [Fact]
    public void 充当額が不足する場合は古い順に配分され残りの行はゼロになる()
    {
        // 明細6,000円+4,000円=10,000円の請求に対し、部分入金5,500円の例（TODO.md 7-1決定1）。
        var actual = SettlementAllocator.Allocate([6000m, 4000m], 5500m);
        Assert.Equal([5500m, 0m], actual);
    }

    [Fact]
    public void 返品行を含み全額入金なら売上行と返品行の両方が配分される()
    {
        var actual = SettlementAllocator.Allocate([10000m, -2000m], 8000m);
        Assert.Equal([10000m, -2000m], actual);
    }

    [Fact]
    public void 返品行を含む一部入金では正の行だけに古い順で配分され返品行は据え置く()
    {
        var actual = SettlementAllocator.Allocate([10000m, -2000m], 5000m);
        Assert.Equal([5000m, 0m], actual);
    }

    [Fact]
    public void 充当額がマイナスなら同符号の返品行にだけ配分される()
    {
        var actual = SettlementAllocator.Allocate([10000m, -2000m], -1500m);
        Assert.Equal([0m, -1500m], actual);
    }

    [Fact]
    public void 対象額の合計がゼロでも充当額がゼロなら何も配分しない()
    {
        // 素朴な「マイナス行を先に全額充当」方式だと pool=0 でも返品行が消込完了になる事故が
        // 起きる（SalesEditLockEvaluatorの編集ロック条件4に波及する）。その回帰テスト。
        var actual = SettlementAllocator.Allocate([10000m, -10000m], 0m);
        Assert.Equal([0m, 0m], actual);
    }

    [Fact]
    public void 対象額の合計がゼロで充当額もゼロでない場合は不足扱いで古い順に配分する()
    {
        var actual = SettlementAllocator.Allocate([10000m, -10000m], 3000m);
        Assert.Equal([3000m, 0m], actual);
    }

    [Fact]
    public void 対象額がゼロの行は配分対象にならない()
    {
        var actual = SettlementAllocator.Allocate([0m, 5000m], 5000m);
        Assert.Equal([0m, 5000m], actual);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(6000)]
    [InlineData(20000)]
    public void 各行の配分額は対象額を絶対値で超えない(double pool)
    {
        var targets = new[] { 6000m, 4000m, -1000m };
        var actual = SettlementAllocator.Allocate(targets, (decimal)pool);
        for (var i = 0; i < targets.Length; i++)
        {
            Assert.True(Math.Abs(actual[i]) <= Math.Abs(targets[i]));
        }
    }

    [Fact]
    public void 同じ入力を二度渡しても同じ結果になる()
    {
        var targets = new[] { 6000m, 4000m };
        var first = SettlementAllocator.Allocate(targets, 5500m);
        var second = SettlementAllocator.Allocate(targets, 5500m);
        Assert.Equal(first, second);
    }

    [Fact]
    public void 空のリストを渡しても例外にならない()
    {
        var actual = SettlementAllocator.Allocate([], 5000m);
        Assert.Empty(actual);
    }
}
