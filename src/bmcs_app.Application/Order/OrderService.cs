using bmcs_app.Application.Common;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Order;

/// <summary>受注入力のユースケース（TODO.md 4-3）。仮伝票の新規登録のみを扱う。</summary>
public class OrderService(
    BmcsDbContext dbContext,
    SlipNumberService slipNumberService,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<OrderService> logger)
{
    /// <summary>
    /// 受注を新規登録する。伝票番号の採番と明細行の登録を同一トランザクションで行う
    /// （docs/architecture.md 6章）。呼び出し順は固定: エンティティ組み立て（呼び出し元）
    /// → トランザクション開始 → 採番 → 番号を明細行へ代入 → SaveChangesAsync(1回) → コミット。
    /// </summary>
    /// <param name="lines">
    /// 保存対象の明細行。<see cref="OrderSlip.OrderSlipNumber"/> は仮値でよい（採番後に上書きする）。
    /// </param>
    public async Task<string> CreateAsync(IReadOnlyList<OrderSlip> lines, CancellationToken cancellationToken = default)
    {
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var orderSlipNumber = await slipNumberService.NextAsync(SlipNumberKind.OrderSlip, cancellationToken);

        foreach (var line in lines)
        {
            line.OrderSlipNumber = orderSlipNumber;
            line.CreatedBy = employeeCode;
            line.CreatedAt = now;
            line.UpdatedBy = employeeCode;
            line.UpdatedAt = now;
            line.IsDeleted = false;
        }

        dbContext.OrderSlips.AddRange(lines);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "受注を登録しました。OrderSlipNumber={OrderSlipNumber} 行数={LineCount}",
            orderSlipNumber, lines.Count);

        return orderSlipNumber;
    }
}
