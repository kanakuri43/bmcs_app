using bmcs_app.Domain.Entities;
using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 売上伝票の編集可否（C-6・2026-09-10確定）の3条件による判定。
/// ①請求締め（<see cref="Sales.BillingNumber"/> が確定済み <c>billing</c> を指す）
/// ②月次締め（対象年月の <c>monthly_closing</c> が確定済み）
/// ③入金済み（<see cref="Sales.SettlementStatus"/> が消込完了）
/// のいずれかに該当する行が1件でもあれば伝票全体を編集不可とする（`docs/product-spec.md` 共通業務ルール5）。
/// DBアクセス（<c>monthly_closing</c> の照会）は呼び出し元（<c>SalesEditLockService</c>）の責務とし、
/// 本クラスは純粋関数として単体テスト可能にする。
/// </summary>
public static class SalesEditLockEvaluator
{
    /// <summary>
    /// <paramref name="lines"/> は同一伝票の明細行（1件以上）。<paramref name="monthlyClosingConfirmed"/> は
    /// 当該得意先・当該年月の <c>monthly_closing</c> が確定済みかどうか（呼び出し元が事前に照会する）。
    /// </summary>
    public static SalesEditLock Evaluate(IReadOnlyList<Sales> lines, bool monthlyClosingConfirmed)
    {
        if (lines.Any(l => l.BillingNumber is not null))
        {
            return new SalesEditLock(true, "請求締め済みのため編集できません。");
        }

        if (monthlyClosingConfirmed)
        {
            return new SalesEditLock(true, "月次締め済みのため編集できません。");
        }

        if (lines.Any(l => l.SettlementStatus == SettlementStatus.FullySettled))
        {
            return new SalesEditLock(true, "入金済み（消込完了）のため編集できません。");
        }

        return SalesEditLock.Unlocked;
    }
}

/// <summary>編集可否の判定結果。<see cref="Reason"/> はロック中のみ非null（ViewModel はこれを表示するだけ）。</summary>
public readonly record struct SalesEditLock(bool IsLocked, string? Reason)
{
    public static SalesEditLock Unlocked { get; } = new(false, null);
}
