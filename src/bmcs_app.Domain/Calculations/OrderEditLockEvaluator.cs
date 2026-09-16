using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 受注伝票の編集可否（TODO.md 4-6、2026-09-16確定）の判定。受注は請求・消込の対象外のため
/// <see cref="SalesEditLockEvaluator"/> の4条件（請求締め・明細請求書発行・月次締め・入金済み）は
/// 適用されない（docs/database-schema.md 1章「受注は状態にかかわらず常に直接修正可能」はC-6の
/// 当初決定だったが、2026-09-16のユーザー確認により「未売上（<see cref="OrderStatus.NotSold"/>）の
/// 伝票のみ直接修正可」に改訂された）。判定に必要な情報はすべて明細行自身の
/// <see cref="OrderSlip.OrderStatus"/> にあり、売上のような外部テーブル照会（monthly_closing等）が
/// 不要なため、<c>SalesEditLockService</c> に相当するApplication層のラッパーは作らない
/// （呼び出し元は <see cref="Application.Order.OrderService"/> とViewModelの両方から直接呼ぶ）。
/// </summary>
public static class OrderEditLockEvaluator
{
    /// <summary><paramref name="lines"/> は同一伝票の明細行（1件以上）。</summary>
    public static OrderEditLock Evaluate(IReadOnlyList<OrderSlip> lines)
    {
        if (lines.Any(l => l.OrderStatus == OrderStatus.Cancelled))
        {
            return new OrderEditLock(true, "中止済みのため修正できません。");
        }

        if (lines.Any(l => l.OrderStatus == OrderStatus.FullySold))
        {
            return new OrderEditLock(true, "売上完了済みの明細行を含むため修正できません。");
        }

        if (lines.Any(l => l.OrderStatus == OrderStatus.PartiallySold))
        {
            return new OrderEditLock(true, "一部売上済みの明細行を含むため修正できません。");
        }

        return OrderEditLock.Unlocked;
    }
}

/// <summary>編集可否の判定結果。<see cref="Reason"/> はロック中のみ非null（ViewModel はこれを表示するだけ）。</summary>
public readonly record struct OrderEditLock(bool IsLocked, string? Reason)
{
    public static OrderEditLock Unlocked { get; } = new(false, null);
}
