using bmcs_app.Application.Common;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace bmcs_app.Application.Order;

/// <summary>受注入力のユースケース。仮伝票の新規登録と、既存受注の直接修正を扱う。</summary>
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

    /// <summary>
    /// 既存の受注伝票を直接修正する。受注は
    /// 「未売上（<see cref="OrderStatus.NotSold"/>）の伝票のみ直接修正可」である（<see cref="OrderEditLockEvaluator"/>）。行の追加・更新・削除（論理削除）を1回の呼び出しで
    /// まとめて扱う。<see cref="bmcs_app.Application.Sales.SalesService.UpdateAsync"/> と同じ構成だが、
    /// 受注には税額確定・消込再計算・受注デルタ適用に相当する処理がないため、それらは行わない。
    /// </summary>
    /// <param name="orderSlipNumber">対象の受注伝票番号。</param>
    /// <param name="lines">
    /// 保存後にあるべき明細行の集合。既存行は読込時と同じ <see cref="OrderSlip.LineNumber"/> を
    /// 維持すること（呼び出し元は行番号を詰め直してはいけない）。新規追加する行は
    /// <see cref="OrderSlip.LineNumber"/> に <c>0</c> を設定する（本メソッドが採番する）。
    /// 読込時にあった行番号のうち <paramref name="lines"/> に含まれないものは論理削除する。
    /// <see cref="OrderSlip.CustomerCode"/> は無視する（得意先の変更は本メソッドの範囲外）。
    /// </param>
    /// <param name="loadedLineNumbers">
    /// 画面が伝票を読み込んだ時点の明細行番号の集合。保存直前に再取得した現在の行番号集合と比較し、
    /// 他のユーザーによる行の追加・削除を検出する（docs/architecture.md 9章）。
    /// </param>
    /// <remarks>
    /// 明示トランザクションは開始しない。採番せず、他サービスも呼ばず、<c>SaveChangesAsync</c> は
    /// 1回のみのため、docs/architecture.md 6章「1ユースケース＝1回の SaveChangesAsync。明示的な
    /// トランザクションは不要」の原則どおり暗黙トランザクションで足りる（前例:
    /// <see cref="OrderStatusService.CancelSlipAsync"/>）。
    /// <see cref="bmcs_app.Application.Sales.SalesService.UpdateAsync"/> が明示トランザクションを張るのは、
    /// 消込再計算のために <c>SaveChangesAsync</c> が2回になるからであり、受注にはその理由がない。
    /// </remarks>
    /// <exception cref="OrderOperationException">
    /// 対象の受注が存在しない、未売上でない明細行を含む、存在しない明細行番号が指定されている、
    /// または保存後に明細行が1件も残らない（全行削除）場合。
    /// </exception>
    /// <exception cref="SlipConcurrencyException">他のユーザーが同じ伝票の明細行を追加・削除していた場合。</exception>
    public async Task UpdateAsync(
        string orderSlipNumber,
        IReadOnlyList<OrderSlip> lines,
        IReadOnlyList<short> loadedLineNumbers,
        CancellationToken cancellationToken = default)
    {
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        var currentLines = await dbContext.OrderSlips
            .Where(o => o.OrderSlipNumber == orderSlipNumber && !o.IsDeleted)
            .ToListAsync(cancellationToken);

        if (currentLines.Count == 0)
        {
            throw new OrderOperationException($"受注が見つかりません。OrderSlipNumber={orderSlipNumber}");
        }

        SlipConcurrencyGuard.EnsureLineSetUnchanged(loadedLineNumbers, currentLines.Select(l => l.LineNumber).ToList());

        var lockResult = OrderEditLockEvaluator.Evaluate(currentLines);
        if (lockResult.IsLocked)
        {
            throw new OrderOperationException(lockResult.Reason!);
        }

        var currentByLineNumber = currentLines.ToDictionary(l => l.LineNumber);
        var incomingKept = lines.Where(l => l.LineNumber != 0).ToList();
        var incomingNew = lines.Where(l => l.LineNumber == 0).ToList();

        var keptLineNumbers = incomingKept.Select(l => l.LineNumber).ToHashSet();
        if (incomingKept.Any(l => !currentByLineNumber.ContainsKey(l.LineNumber)))
        {
            throw new OrderOperationException("存在しない明細行番号が指定されています。");
        }

        if (keptLineNumbers.Count + incomingNew.Count == 0)
        {
            throw new OrderOperationException("訂正で全行を削除することはできません。中止（F8）をご利用ください。");
        }

        // 読込時にあったが今回の一覧に含まれない行は論理削除する（物理削除しない）。
        foreach (var current in currentLines.Where(l => !keptLineNumbers.Contains(l.LineNumber)))
        {
            current.IsDeleted = true;
        }

        // 既存行を更新する（ホワイトリスト方式。得意先コード・引当数量・状態カラム・監査列は対象外）。
        foreach (var incoming in incomingKept)
        {
            var current = currentByLineNumber[incoming.LineNumber];
            ApplyLineValues(current, incoming);
        }

        // 新規行を追加する。
        var nextLineNumber = (short)(currentLines.Max(l => l.LineNumber) + 1);
        foreach (var incoming in incomingNew)
        {
            incoming.OrderSlipNumber = orderSlipNumber;
            incoming.LineNumber = nextLineNumber++;
            incoming.AllocatedQuantity = 0m;
            incoming.OrderStatus = OrderStatus.NotSold;
            incoming.SalesConfirmedQuantity = 0m;
            incoming.CreatedBy = employeeCode;
            incoming.CreatedAt = now;
            incoming.IsDeleted = false;
            dbContext.OrderSlips.Add(incoming);
        }

        // 読み込んだ全行（削除された行を含む）を更新対象に含め、rowversion の照合を全行で効かせる
        // （docs/architecture.md 9章）。取消済み行を含めても SaveChanges 自体には影響しない。
        SlipConcurrencyGuard.TouchAll(currentLines, employeeCode, now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SlipConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        logger.LogInformation("受注を訂正しました。OrderSlipNumber={OrderSlipNumber}", orderSlipNumber);
    }

    /// <summary>
    /// 訂正時に上書きしてよい列だけを明示的にコピーする（ホワイトリスト方式）。
    /// 得意先コード（変更不可）・宛名ID（UIが値を持たず常にnull。将来復活時に静かに消えるのを防ぐ）・
    /// 引当数量（在庫連携スコープ外）・状態カラム（所有者は<see cref="OrderStatusService"/>のみ）・
    /// 監査列・rowversionは対象外とする。<see cref="AuditableEntity.IsDeleted"/> は明示的に
    /// <c>false</c> に戻す（保存失敗後の再保存でChangeTrackerが汚れたまま残った場合の対策）。
    /// </summary>
    private static void ApplyLineValues(OrderSlip current, OrderSlip incoming)
    {
        current.OrderDate = incoming.OrderDate;
        current.CustomerName = incoming.CustomerName;
        current.ProductCode = incoming.ProductCode;
        current.ProductName = incoming.ProductName;
        current.Specification = incoming.Specification;
        current.UnitName = incoming.UnitName;
        current.OrderQuantity = incoming.OrderQuantity;
        current.UnitPrice = incoming.UnitPrice;
        current.Amount = incoming.Amount;
        current.CostPrice = incoming.CostPrice;
        current.TaxCategory = incoming.TaxCategory;
        current.TaxRate = incoming.TaxRate;
        current.SlipRemarks = incoming.SlipRemarks;
        current.LineRemarks = incoming.LineRemarks;
        current.InternalRemarks = incoming.InternalRemarks;
        current.EmployeeCode = incoming.EmployeeCode;
        current.IsDeleted = false;
    }
}
