using bmcs_app.Application.Common;
using bmcs_app.Domain.Enums;
using bmcs_app.Domain.Numbering;
using bmcs_app.Infrastructure;
using bmcs_app.Infrastructure.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Common;

/// <summary>
/// 採番（TODO.md 4-1）の結合テスト。開発用ライブDB（172.16.3.171）に対して実行する
/// （docs/architecture.md 16章）。完了条件「同時登録でも採番が重複しない」の実証。
/// </summary>
public class SlipNumberServiceTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    private const int ParallelCount = 32;

    [Fact]
    public async Task 同時採番しても重複せず欠番も出ない()
    {
        var baseline = await ReadCurrentValueAsync(DevDatabaseFixture.TestSequenceKey);

        var results = await Task.WhenAll(
            Enumerable.Range(0, ParallelCount).Select(_ => IncrementTestKeyInOwnTransactionAsync()));

        // (a) 重複がない
        Assert.Equal(ParallelCount, results.Distinct().Count());

        // (b) 欠番がない＝baseline+1 .. baseline+ParallelCount の連続した集合と完全一致
        var expected = Enumerable.Range(1, ParallelCount).Select(i => baseline + i);
        Assert.Equal(expected.OrderBy(v => v), results.OrderBy(v => v));

        // (c) current_value が実際に ParallelCount 件分進んでいる
        var after = await ReadCurrentValueAsync(DevDatabaseFixture.TestSequenceKey);
        Assert.Equal(baseline + ParallelCount, after);
    }

    [Fact]
    public async Task ロールバックした採番は欠番にならず同じ値が再発行される()
    {
        var baseline = await ReadCurrentValueAsync(DevDatabaseFixture.TestSequenceKey);

        var firstAttempt = await IncrementTestKeyThenRollbackAsync();
        var secondAttempt = await IncrementTestKeyThenRollbackAsync();

        Assert.Equal(baseline + 1, firstAttempt);
        Assert.Equal(baseline + 1, secondAttempt); // ロールバックされたので同じ番号が再発行される

        var after = await ReadCurrentValueAsync(DevDatabaseFixture.TestSequenceKey);
        Assert.Equal(baseline, after); // current_value は変化していない（欠番も残らない）
    }

    [Fact]
    public async Task 実キーでの採番結果が8桁ゼロ埋め文字列で返る()
    {
        var sequenceKey = SlipNumberFormatter.ToSequenceKey(SlipNumberKind.OrderSlip);
        var baseline = await ReadCurrentValueAsync(sequenceKey);

        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var slipNumberService = scope.ServiceProvider.GetRequiredService<SlipNumberService>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var actual = await slipNumberService.NextAsync(SlipNumberKind.OrderSlip);
        await transaction.RollbackAsync(); // order_slip の実キーは消費しない

        Assert.Equal(SlipNumberFormatter.Format(baseline + 1), actual);
    }

    [Fact]
    public async Task トランザクション外での採番は例外になる()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var slipNumberService = scope.ServiceProvider.GetRequiredService<SlipNumberService>();

        // BeginTransactionAsync を呼んでいない状態で採番を試みる。
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => slipNumberService.NextAsync(SlipNumberKind.SalesSlip));
    }

    private async Task<long> IncrementTestKeyInOwnTransactionAsync()
    {
        // DbContext はスレッドセーフでないため、並列タスクごとに独立したスコープ（＝独立した
        // DbContext）を使う（docs/architecture.md 8章）。
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var command = scope.ServiceProvider.GetRequiredService<SlipNumberSequenceCommand>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var value = await command.IncrementAsync(DevDatabaseFixture.TestSequenceKey, "TEST");
        await transaction.CommitAsync();

        return value;
    }

    private async Task<long> IncrementTestKeyThenRollbackAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var command = scope.ServiceProvider.GetRequiredService<SlipNumberSequenceCommand>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var value = await command.IncrementAsync(DevDatabaseFixture.TestSequenceKey, "TEST");
        await transaction.RollbackAsync();

        return value;
    }

    private async Task<long> ReadCurrentValueAsync(string sequenceKey)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();

        return await dbContext.SlipNumberSequences
            .AsNoTracking()
            .Where(s => s.SequenceKey == sequenceKey)
            .Select(s => s.CurrentValue)
            .SingleAsync();
    }
}
