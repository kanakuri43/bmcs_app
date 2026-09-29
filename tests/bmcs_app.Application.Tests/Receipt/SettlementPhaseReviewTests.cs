using bmcs_app.Application.Receipt;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace bmcs_app.Application.Tests.Receipt;

/// <summary>
/// Phase 7-6「フェーズレビュー（消込整合性）」の結合テスト。開発用ライブDB（172.16.3.171）に
/// 対して実行する（docs/architecture.md 16章）。完了条件「キャッシュ列と入金明細の実集計が
/// 全パターンで一致する」を、個々のシナリオ単体テスト（<see cref="SettlementServiceTests"/>）
/// ではなく、**DBに現存する全得意先**に対して直接検証する。
/// </summary>
/// <remarks>
/// <see cref="SettlementService.RecalculateForBillingGroupAsync"/>は「値が実際に変わった行だけ」を
/// 更新する設計（<c>docs/architecture.md</c> 9章）であるため、既存データに対して呼び出しても
/// キャッシュ列が実態と一致していれば更新件数は0のはず。得意先ごとにトランザクションを開始して
/// 呼び出し、更新件数が0であることを確認したうえで必ずRollbackする（seed・実データを一切変更
/// しない。<c>SettlementServiceTests</c>と同じ「外側トランザクション+Rollback」方式）。
/// </remarks>
public class SettlementPhaseReviewTests(DevDatabaseFixture fixture) : IClassFixture<DevDatabaseFixture>
{
    [Fact]
    public async Task 既存の全得意先で消込キャッシュ列は実態と一致しており再計算しても差分が出ない()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmcsDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<SettlementService>();

        var customerCodes = await dbContext.Customers.AsNoTracking()
            .Select(c => c.CustomerCode)
            .ToListAsync();

        var driftedCustomers = new List<string>();

        foreach (var customerCode in customerCodes)
        {
            dbContext.ChangeTracker.Clear();

            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            var result = await service.RecalculateForBillingGroupAsync(customerCode);
            await transaction.RollbackAsync();

            if (result.UpdatedSalesLineCount != 0 || result.UpdatedReceiptLineCount != 0)
            {
                driftedCustomers.Add(
                    $"{customerCode}（売上{result.UpdatedSalesLineCount}行／入金{result.UpdatedReceiptLineCount}行）");
            }
        }

        Assert.True(driftedCustomers.Count == 0, "キャッシュ列に差分が検出された得意先: " + string.Join(", ", driftedCustomers));
    }
}
