using bmcs_app.Application.Common;
using bmcs_app.Application.Order;
using bmcs_app.Application.Receipt;
using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SalesEntity = bmcs_app.Domain.Entities.Sales;

namespace bmcs_app.Application.Sales;

/// <summary>
/// 売上入力のユースケース（TODO.md 5-2・5-3・5-6）。新規登録（都度売上・受注からの売上確定）と、
/// 既存伝票の訂正・取消を扱う。<see cref="OrderService"/>（受注入力）と同じ構成だが、
/// 保存前に税額カラムを確定する点が異なる（<c>order_slip</c> は税額列を持たないため）。
/// </summary>
public class SalesService(
    BmcsDbContext dbContext,
    SlipNumberService slipNumberService,
    OrderStatusService orderStatusService,
    SalesEditLockService salesEditLockService,
    SettlementService settlementService,
    ICurrentEmployeeContext currentEmployeeContext,
    ILogger<SalesService> logger)
{
    /// <summary>
    /// 売上を新規登録する。伝票番号の採番・税額カラムの確定・明細行の登録を同一トランザクションで行う
    /// （docs/architecture.md 6章）。呼び出し順は固定: エンティティ組み立て（呼び出し元）
    /// → トランザクション開始 → 採番 → 番号を明細行へ代入 → 受注デルタの適用（受注由来の行がある場合）
    /// → 税額確定（<see cref="SalesTaxAmountAssigner"/>）→ SaveChangesAsync(1回) → コミット。
    /// </summary>
    /// <param name="lines">
    /// 保存対象の明細行。<see cref="SalesEntity.SalesSlipNumber"/> は仮値でよい（採番後に上書きする）。
    /// <see cref="SalesEntity.SlipTaxAmount"/>／<see cref="SalesEntity.TaxAmount"/> は未設定でよい
    /// （本メソッドが確定する）。すべて同一の <see cref="SalesEntity.TaxUnit"/> を持つこと。
    /// 受注からの売上確定（TODO.md 5-3）の場合、該当行に <see cref="SalesEntity.OrderSlipNumber"/>／
    /// <see cref="SalesEntity.OrderLineNumber"/> を設定しておくと、本メソッドが受注側の
    /// 売上化済数量を同一トランザクション・同一 SaveChangesAsync で更新する
    /// （<see cref="OrderStatusService.ApplySalesQuantityDeltasAsync"/>）。受注に紐付けられるのは
    /// <see cref="SlipType.Sales"/> の行のみ（返品・値引行を受注に紐付けることはできない）。
    /// </param>
    /// <param name="roundingType">得意先マスタの端数区分。税額計算に使う。</param>
    /// <exception cref="SalesOperationException">返品・値引行が受注に紐付けられている場合。</exception>
    public async Task<string> CreateAsync(
        IReadOnlyList<SalesEntity> lines, RoundingType roundingType, CancellationToken cancellationToken = default)
    {
        ValidateOrderLinkRestrictedToSalesType(lines);
        ValidateQuantityAmountSignConsistency(lines);

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var salesSlipNumber = await slipNumberService.NextAsync(SlipNumberKind.SalesSlip, cancellationToken);

        foreach (var line in lines)
        {
            line.SalesSlipNumber = salesSlipNumber;
            line.BillingNumber = null; // 新規登録は常に未請求（既存の請求紐付けを消す経路はここではない）
            line.CreatedBy = employeeCode;
            line.CreatedAt = now;
            line.UpdatedBy = employeeCode;
            line.UpdatedAt = now;
            line.IsDeleted = false;
        }

        var orderDeltas = BuildOrderDeltas(lines.Select(l => (l.OrderSlipNumber, l.OrderLineNumber, SignedQuantity(l))));
        if (orderDeltas.Count > 0)
        {
            await orderStatusService.ApplySalesQuantityDeltasAsync(orderDeltas, cancellationToken);
        }

        SalesTaxAmountAssigner.Assign(lines, roundingType);

        dbContext.Sales.AddRange(lines);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "売上を登録しました。SalesSlipNumber={SalesSlipNumber} 行数={LineCount}",
            salesSlipNumber, lines.Count);

        return salesSlipNumber;
    }

    /// <summary>
    /// 既存の売上伝票を訂正する（TODO.md 5-6）。元伝票の直接修正（C-6）であり、赤伝は発行しない。
    /// 行の追加・更新・削除（論理削除）を1回の呼び出しでまとめて扱う。
    /// </summary>
    /// <param name="salesSlipNumber">対象の売上伝票番号。</param>
    /// <param name="lines">
    /// 保存後にあるべき明細行の集合。既存行は読込時と同じ <see cref="SalesEntity.LineNumber"/> を
    /// 維持すること（呼び出し元は行番号を詰め直してはいけない）。新規追加する行は
    /// <see cref="SalesEntity.LineNumber"/> に <c>0</c> を設定する（本メソッドが採番する）。
    /// 読込時にあった行番号のうち <paramref name="lines"/> に含まれないものは論理削除する。
    /// </param>
    /// <param name="roundingType">得意先マスタの端数区分。</param>
    /// <param name="loadedLineNumbers">
    /// 画面が伝票を読み込んだ時点の明細行番号の集合（<see cref="SalesEditLockService"/>と同じ理由で
    /// 呼び出し元が読込時にキャプチャしておく）。保存直前に再取得した現在の行番号集合と比較し、
    /// 他のユーザーによる行の追加・削除を検出する（docs/architecture.md 9章）。
    /// </param>
    /// <exception cref="SalesOperationException">
    /// 対象の売上が存在しない、編集ロック（C-6の3条件）に該当する場合、または返品・値引行が
    /// 受注に紐付けられている場合。訂正後の伝票日付が新たに編集ロック対象の年月になる場合も含む。
    /// </exception>
    /// <exception cref="SlipConcurrencyException">
    /// 他のユーザーが同じ伝票の明細行を追加・削除・更新していた場合。
    /// </exception>
    public async Task UpdateAsync(
        string salesSlipNumber,
        IReadOnlyList<SalesEntity> lines,
        RoundingType roundingType,
        IReadOnlyList<short> loadedLineNumbers,
        CancellationToken cancellationToken = default)
    {
        ValidateOrderLinkRestrictedToSalesType(lines);
        ValidateQuantityAmountSignConsistency(lines);

        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var currentLines = await dbContext.Sales
            .Where(s => s.SalesSlipNumber == salesSlipNumber && !s.IsDeleted)
            .ToListAsync(cancellationToken);

        if (currentLines.Count == 0)
        {
            throw new SalesOperationException($"売上が見つかりません。SalesSlipNumber={salesSlipNumber}");
        }

        SlipConcurrencyGuard.EnsureLineSetUnchanged(loadedLineNumbers, currentLines.Select(l => l.LineNumber).ToList());

        // 訂正前（現状）の状態で編集ロックを確認する。
        var lockResultBefore = await salesEditLockService.EvaluateAsync(currentLines, cancellationToken);
        if (lockResultBefore.IsLocked)
        {
            throw new SalesOperationException(lockResultBefore.Reason!);
        }

        var currentByLineNumber = currentLines.ToDictionary(l => l.LineNumber);
        var incomingKept = lines.Where(l => l.LineNumber != 0).ToList();
        var incomingNew = lines.Where(l => l.LineNumber == 0).ToList();

        var keptLineNumbers = incomingKept.Select(l => l.LineNumber).ToHashSet();
        if (incomingKept.Any(l => !currentByLineNumber.ContainsKey(l.LineNumber)))
        {
            throw new SalesOperationException("存在しない明細行番号が指定されています。");
        }

        var deltaContributions = new List<(string? OrderSlipNumber, short? OrderLineNumber, decimal Delta)>();
        var nextLineNumber = (short)(currentLines.Max(l => l.LineNumber) + 1);

        // 読込時にあったが今回の一覧に含まれない行は論理削除する（M-17: 物理削除しない）。
        foreach (var current in currentLines.Where(l => !keptLineNumbers.Contains(l.LineNumber)))
        {
            deltaContributions.Add((current.OrderSlipNumber, current.OrderLineNumber, -SignedQuantity(current)));
            current.IsDeleted = true;
        }

        // 既存行を更新する（デルタは更新前の数量を使うため、値の上書きより先に計算する）。
        foreach (var incoming in incomingKept)
        {
            var current = currentByLineNumber[incoming.LineNumber];
            var orderSlipNumber = incoming.OrderSlipNumber ?? current.OrderSlipNumber;
            var orderLineNumber = incoming.OrderLineNumber ?? current.OrderLineNumber;
            deltaContributions.Add((orderSlipNumber, orderLineNumber, SignedQuantity(incoming) - SignedQuantity(current)));
            ApplyLineValues(current, incoming);
        }

        // 新規行を追加する。
        foreach (var incoming in incomingNew)
        {
            incoming.SalesSlipNumber = salesSlipNumber;
            incoming.LineNumber = nextLineNumber++;
            incoming.BillingNumber = null;
            incoming.BillingStatus = BillingLinkStatus.Unbilled;
            incoming.SettlementStatus = SettlementStatus.Unsettled;
            incoming.SettledAmount = 0m;
            incoming.DeliveryNoteIssueCount = 0;
            incoming.CreatedBy = employeeCode;
            incoming.CreatedAt = now;
            incoming.IsDeleted = false;
            deltaContributions.Add((incoming.OrderSlipNumber, incoming.OrderLineNumber, SignedQuantity(incoming)));
            dbContext.Sales.Add(incoming);
        }

        var orderDeltas = BuildOrderDeltas(deltaContributions);
        if (orderDeltas.Count > 0)
        {
            await orderStatusService.ApplySalesQuantityDeltasAsync(orderDeltas, cancellationToken);
        }

        // 読み込んだ全行（削除された行を含む）を更新対象に含め、rowversion の照合を全行で効かせる
        // （docs/architecture.md 9章）。取消済み行を含めても SaveChanges 自体には影響しない。
        SlipConcurrencyGuard.TouchAll(currentLines, employeeCode, now);

        var activeLines = currentLines.Where(l => !l.IsDeleted).Concat(incomingNew).ToList();
        if (activeLines.Count > 0)
        {
            // 訂正後（新状態）で編集ロック対象にならないかも確認する（例: 伝票日付を確定済みの
            // 月次締め年月へ動かす訂正を防ぐ。docs/database-schema.md 1章の条件②）。
            var lockResultAfter = await salesEditLockService.EvaluateAsync(activeLines, cancellationToken);
            if (lockResultAfter.IsLocked)
            {
                throw new SalesOperationException($"訂正後の内容は編集ロック対象になるため保存できません: {lockResultAfter.Reason}");
            }

            SalesTaxAmountAssigner.Assign(activeLines, roundingType);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SlipConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        // 訂正で金額を減らした場合に消込済金額が売上金額を超えて取り残る不整合を防ぐ
        // （TODO.md 7-1）。訂正後の金額に合わせてクランプし直す。
        await settlementService.RecalculateForCustomerAsync(currentLines[0].CustomerCode, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("売上を訂正しました。SalesSlipNumber={SalesSlipNumber}", salesSlipNumber);
    }

    /// <summary>
    /// 売上伝票を取消する（TODO.md 5-6）。全明細行を論理削除し（M-17）、受注由来の行があれば
    /// 受注側の売上化済数量を逆遷移させる。C-6の3条件に該当する伝票は取消できない。
    /// </summary>
    /// <exception cref="SalesOperationException">対象の売上が存在しない、または編集ロックに該当する場合。</exception>
    /// <exception cref="SlipConcurrencyException">他のユーザーが同じ伝票を更新済みの場合。</exception>
    public async Task CancelSlipAsync(string salesSlipNumber, CancellationToken cancellationToken = default)
    {
        var employeeCode = currentEmployeeContext.EmployeeCode;
        var now = DateTime.Now;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var lines = await dbContext.Sales
            .Where(s => s.SalesSlipNumber == salesSlipNumber && !s.IsDeleted)
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            throw new SalesOperationException($"売上が見つかりません。SalesSlipNumber={salesSlipNumber}");
        }

        var lockResult = await salesEditLockService.EvaluateAsync(lines, cancellationToken);
        if (lockResult.IsLocked)
        {
            throw new SalesOperationException(lockResult.Reason!);
        }

        var orderDeltas = BuildOrderDeltas(lines.Select(l => (l.OrderSlipNumber, l.OrderLineNumber, -SignedQuantity(l))));
        if (orderDeltas.Count > 0)
        {
            await orderStatusService.ApplySalesQuantityDeltasAsync(orderDeltas, cancellationToken);
        }

        foreach (var line in lines)
        {
            line.IsDeleted = true;
            line.UpdatedBy = employeeCode;
            line.UpdatedAt = now;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new SlipConcurrencyException("他のユーザーが更新しました。再読み込みしてください。", ex);
        }

        // 取消した行自体は再計算対象から外れる（RecalculateForCustomerAsyncはIsDeleted行を
        // 読まない）が、同じ請求／明細請求書に属する他の売上明細行への配分が取消によって
        // 変わりうるため再計算する（TODO.md 7-1）。
        await settlementService.RecalculateForCustomerAsync(lines[0].CustomerCode, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "売上を取消しました。SalesSlipNumber={SalesSlipNumber} 行数={LineCount}", salesSlipNumber, lines.Count);
    }

    /// <summary>区分が「値引」「返品」の行は受注消化に算入しない（売上行のみが対象）。</summary>
    private static decimal SignedQuantity(SalesEntity line) => line.SlipType == SlipType.Sales ? line.Quantity : 0m;

    /// <summary>
    /// 受注デルタの寄与分を (受注伝票番号, 受注行番号) ごとに集約する。同一の受注明細行に対する
    /// 寄与が複数行から生じても（例: 1つの受注行を2回に分けて売上確定する）
    /// <see cref="OrderStatusService.ApplySalesQuantityDeltasAsync"/> が重複キーで拒否しないよう、
    /// ここで合算してから渡す。受注伝票番号・受注行番号は両方設定するか両方未設定でなければならない。
    /// </summary>
    /// <exception cref="SalesOperationException">受注伝票番号・受注行番号の一方だけが設定されている場合。</exception>
    private static List<OrderLineQuantityDelta> BuildOrderDeltas(
        IEnumerable<(string? OrderSlipNumber, short? OrderLineNumber, decimal Delta)> contributions)
    {
        var grouped = new Dictionary<(string OrderSlipNumber, short OrderLineNumber), decimal>();

        foreach (var (orderSlipNumber, orderLineNumber, delta) in contributions)
        {
            if (orderSlipNumber is null != orderLineNumber is null)
            {
                throw new SalesOperationException(
                    "受注伝票番号と受注行番号は両方設定するか、両方とも未設定にしてください。");
            }

            if (orderSlipNumber is null || orderLineNumber is null || delta == 0m)
            {
                continue;
            }

            var key = (orderSlipNumber, orderLineNumber.Value);
            grouped[key] = grouped.GetValueOrDefault(key) + delta;
        }

        return grouped
            .Where(kv => kv.Value != 0m)
            .Select(kv => new OrderLineQuantityDelta(kv.Key.OrderSlipNumber, kv.Key.OrderLineNumber, kv.Value))
            .ToList();
    }

    /// <summary>
    /// 返品・値引行が受注に紐付けられていないことを検証する。受注の売上化済数量を減算するのは
    /// 「売上」行のみであり（<see cref="SignedQuantity"/>）、返品・値引による受注消化の取消は
    /// 元の売上行を直接訂正することで行う（TODO.md 5-6）。
    /// </summary>
    private static void ValidateOrderLinkRestrictedToSalesType(IReadOnlyList<SalesEntity> lines)
    {
        if (lines.Any(l => l.OrderSlipNumber is not null && l.SlipType != SlipType.Sales))
        {
            throw new SalesOperationException("返品・値引行を受注に紐付けることはできません。");
        }
    }

    /// <summary>
    /// 数量と金額の符号が一致していることを検証する（金額が狂う経路を構造的に防ぐ。TODO.md 5-4）。
    /// 空行やゼロ数量行は対象外。
    /// </summary>
    private static void ValidateQuantityAmountSignConsistency(IReadOnlyList<SalesEntity> lines)
    {
        if (lines.Any(l => l.Quantity != 0m && l.Amount != 0m && Math.Sign(l.Quantity) != Math.Sign(l.Amount)))
        {
            throw new SalesOperationException("数量と金額の符号が一致しない明細行があります。");
        }
    }

    /// <summary>
    /// 訂正時に上書きしてよい列だけを明示的にコピーする（ホワイトリスト方式）。
    /// 得意先コード・税区分・請求/消込関連の状態カラム・監査列は対象外とする
    /// （得意先の変更や請求紐付けの改変は、行編集の範囲を超えるため）。
    /// <see cref="Domain.Entities.AuditableEntity.IsDeleted"/> は明示的に <c>false</c> に戻す
    /// （保存失敗後に同じ画面から再保存すると、ChangeTrackerに残った汚れた値
    /// （直前の失敗した保存で立てた <c>true</c>）がそのまま上書きされず論理削除される潜在バグの対策。
    /// TODO.md 5-6レビューで発見）。
    /// </summary>
    private static void ApplyLineValues(SalesEntity current, SalesEntity incoming)
    {
        current.SlipDate = incoming.SlipDate;
        current.CustomerName = incoming.CustomerName;
        current.SlipType = incoming.SlipType;
        current.ProductCode = incoming.ProductCode;
        current.ProductName = incoming.ProductName;
        current.Specification = incoming.Specification;
        current.UnitName = incoming.UnitName;
        current.Quantity = incoming.Quantity;
        current.UnitPrice = incoming.UnitPrice;
        current.Amount = incoming.Amount;
        current.CostPrice = incoming.CostPrice;
        current.TaxCategory = incoming.TaxCategory;
        current.TaxRate = incoming.TaxRate;
        current.OrderSlipNumber = incoming.OrderSlipNumber;
        current.OrderLineNumber = incoming.OrderLineNumber;
        current.SlipRemarks = incoming.SlipRemarks;
        current.LineRemarks = incoming.LineRemarks;
        current.IsDeleted = false;
    }
}

/// <summary>売上伝票の編集に関する業務ルール違反（編集ロック・対象不存在など）。</summary>
public sealed class SalesOperationException(string message) : Exception(message);
