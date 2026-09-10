using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Order;

/// <summary>
/// 受注の状態遷移のユースケース（TODO.md 4-4）。新規登録専用の <see cref="OrderService"/> とは
/// 責務を分ける。
/// </summary>
public class OrderStatusService(
    BmcsDbContext dbContext,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<OrderStatusService> logger)
{
    /// <summary>
    /// 受注明細行の売上化済数量を変更し、<see cref="OrderStatus"/> を再判定する。
    /// 正のデルタ＝売上化、負のデルタ＝売上取消（逆遷移）。両方向を1メソッドで扱う
    /// （分けると同じ検証が二重になるため）。
    /// <see cref="OrderStatus.Cancelled"/> の明細行に対しては、正のデルタ（売上化）は拒否するが、
    /// 負のデルタ（売上取消）は許可する（TODO.md 5-6。一部売上化 → 受注を中止 → その売上を
    /// 後から訂正・取消する、という順序があり得るため）。ただしその場合も状態は
    /// <see cref="OrderStatus.Cancelled"/> のまま維持し、未売上・一部売上へは戻さない
    /// （中止は終端状態。docs/product-spec.md）。
    /// </summary>
    /// <remarks>
    /// トランザクションは開始せず、<c>SaveChangesAsync</c> も呼ばない。呼び出し元
    /// （売上の登録・取消ユースケース）が、伝票登録と同一の <c>SaveChangesAsync</c> 1回に
    /// 含めて保存する（docs/architecture.md 6章「1ユースケース＝1回の SaveChangesAsync」）。
    /// そのため、呼び出し時に明示トランザクションが開始されていることを要求する
    /// （<see cref="bmcs_app.Infrastructure.Numbering.SlipNumberSequenceCommand"/> と同じ理由）。
    /// </remarks>
    /// <exception cref="InvalidOperationException">明示トランザクションが開始されていない場合。</exception>
    /// <exception cref="OrderOperationException">デルタの内容が業務ルールに違反する場合。</exception>
    public async Task ApplySalesQuantityDeltasAsync(
        IReadOnlyList<OrderLineQuantityDelta> deltas, CancellationToken cancellationToken = default)
    {
        if (deltas.Count == 0)
        {
            return;
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "受注の売上化済数量の更新は、売上登録・取消と同一の明示トランザクション内で実行してください" +
                "（dbContext.Database.BeginTransactionAsync() を先に呼び出す）。" +
                "docs/architecture.md 6章を参照。");
        }

        var keys = deltas.Select(d => (d.OrderSlipNumber, LineNumber: d.LineNumber)).ToList();
        if (keys.Distinct().Count() != keys.Count)
        {
            throw new OrderOperationException("同一の受注明細行に対するデルタが重複しています。");
        }

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        foreach (var delta in deltas)
        {
            var line = await dbContext.OrderSlips
                .SingleOrDefaultAsync(
                    o => o.OrderSlipNumber == delta.OrderSlipNumber
                        && o.LineNumber == delta.LineNumber
                        && !o.IsDeleted,
                    cancellationToken)
                ?? throw new OrderOperationException(
                    $"受注明細行が見つかりません。OrderSlipNumber={delta.OrderSlipNumber} LineNumber={delta.LineNumber}");

            if (line.OrderStatus == OrderStatus.Cancelled && delta.QuantityDelta > 0m)
            {
                throw new OrderOperationException(
                    $"中止済みの受注明細行は売上化できません。OrderSlipNumber={delta.OrderSlipNumber} LineNumber={delta.LineNumber}");
            }

            var updatedQuantity = line.SalesConfirmedQuantity + delta.QuantityDelta;
            if (updatedQuantity < 0m)
            {
                throw new OrderOperationException(
                    $"売上化済数量が受注数量の範囲を下回ります（取消しすぎ）。OrderSlipNumber={delta.OrderSlipNumber} LineNumber={delta.LineNumber}");
            }

            if (updatedQuantity > line.OrderQuantity)
            {
                throw new OrderOperationException(
                    $"売上化済数量が受注数量を超えます。OrderSlipNumber={delta.OrderSlipNumber} LineNumber={delta.LineNumber}");
            }

            line.SalesConfirmedQuantity = updatedQuantity;

            // 中止は終端状態であり、負のデルタ（売上取消）で解除しない（docs/product-spec.md）。
            // 中止済み行への負のデルタは、中止後に別途取消される売上の訂正・取消（TODO.md 5-6）から
            // 呼ばれうる（例: 一部売上化 → 受注を中止 → その売上を後から訂正・取消する場合）ため許可するが、
            // 状態は Cancelled のまま維持し、未売上・一部売上へは戻さない。
            if (line.OrderStatus != OrderStatus.Cancelled)
            {
                line.OrderStatus = OrderStatusCalculator.Determine(line.OrderQuantity, updatedQuantity);
            }

            line.UpdatedBy = employeeCode;
            line.UpdatedAt = now;
        }

        logger.LogInformation("受注の売上化済数量を更新しました。件数={Count}", deltas.Count);
    }

    /// <summary>
    /// 受注伝票（同一伝票番号の全明細行）を中止する。伝票単位のみの操作
    /// （TODO.md 4-4 決定。業務上の失注・キャンセルは伝票丸ごとが通常のため）。
    /// 中止は終端状態であり、解除は実装しない（docs/product-spec.md の遷移定義どおり）。
    /// </summary>
    /// <exception cref="OrderOperationException">対象の受注が存在しない、または遷移できない状態の場合。</exception>
    /// <exception cref="OrderConcurrencyException">他のユーザーが同じ受注を更新済みの場合。</exception>
    public async Task CancelSlipAsync(string orderSlipNumber, CancellationToken cancellationToken = default)
    {
        var lines = await dbContext.OrderSlips
            .Where(o => o.OrderSlipNumber == orderSlipNumber && !o.IsDeleted)
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            throw new OrderOperationException($"受注が見つかりません。OrderSlipNumber={orderSlipNumber}");
        }

        if (lines.Any(l => l.OrderStatus == OrderStatus.Cancelled))
        {
            throw new OrderOperationException($"既に中止済みの受注です。OrderSlipNumber={orderSlipNumber}");
        }

        if (lines.Any(l => l.OrderStatus == OrderStatus.FullySold))
        {
            throw new OrderOperationException(
                $"売上完了済みの明細行を含む受注は中止できません。OrderSlipNumber={orderSlipNumber}");
        }

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        foreach (var line in lines)
        {
            line.OrderStatus = OrderStatus.Cancelled;
            line.UpdatedBy = employeeCode;
            line.UpdatedAt = now;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new OrderConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation(
            "受注を中止しました。OrderSlipNumber={OrderSlipNumber} 行数={LineCount}", orderSlipNumber, lines.Count);
    }
}

/// <summary>受注の状態遷移に関する業務ルール違反。</summary>
public sealed class OrderOperationException(string message) : Exception(message);

/// <summary>楽観的排他制御の競合（他のユーザーによる更新）。</summary>
public sealed class OrderConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
