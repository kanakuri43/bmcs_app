namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 入金の充当額（<c>pool</c>）を、複数の対象額（<c>targets</c>）へ配分する（TODO.md 7-1）。
/// 締め入金は請求データ（<c>billing</c>）へ充当した額をその請求に紐づく売上明細行へ配分する
/// 用途、明細入金は明細請求書へ充当した額をその請求書に紐づく売上明細行へ配分する用途で使う。
///
/// 対象額はマイナス（返品・値引行）を含みうる。素朴な「マイナス行を先に全額充当し、その分を
/// 残額に戻す」方式は、<c>pool = 0</c>（入金が無い）でも返品行だけが消込完了になる事故を起こす
/// （<see cref="SalesEditLockEvaluator"/> の編集ロック条件4・明細請求書候補からの除外に波及する
/// ため実害がある）。本クラスは次の2分岐で、この事故を起こさずに全額充当・過入金・不足の
/// 各ケースを扱う。
///
/// 1. <c>pool == 0</c> → 全行 0（入金が無ければ何も消し込まない）。
/// 2. 対象額の合計（<c>netTarget</c>）が <c>pool</c> と同符号かつ <c>|pool| &gt;= |netTarget|</c>
///    → 各行を対象額のとおりに配分する（返品・値引行も含めて全額消込完了になる。消費税分等の
///    超過分は行に載せない）。
/// 3. それ以外（不足） → <c>pool</c> と同符号の行だけに、呼び出し元が整列した順（伝票日付→
///    伝票番号→行番号の古い順）で <c>min(残額, 残対象額)</c> を配分する。符号が異なる行は 0
///    のまま据え置く（安全側。返品行を消し込むには全額充当が必要という業務上の前提に一致する）。
/// </summary>
public static class SettlementAllocator
{
    /// <param name="targets">
    /// 各行の対象額。呼び出し元が配分順（古い順）に整列済みであること。返品・値引行はマイナス。
    /// </param>
    /// <param name="pool">配分する充当額の合計（<c>allocated_amount + fee_adjustment_amount</c>）。</param>
    /// <returns><paramref name="targets"/> と同じ順序・同じ要素数の配分額。</returns>
    public static IReadOnlyList<decimal> Allocate(IReadOnlyList<decimal> targets, decimal pool)
    {
        var result = new decimal[targets.Count];

        if (targets.Count == 0 || pool == 0m)
        {
            return result;
        }

        var netTarget = targets.Sum();
        if (netTarget != 0m && Math.Sign(pool) == Math.Sign(netTarget) && Math.Abs(pool) >= Math.Abs(netTarget))
        {
            for (var i = 0; i < targets.Count; i++)
            {
                result[i] = targets[i];
            }

            return result;
        }

        var remaining = Math.Abs(pool);
        var poolSign = Math.Sign(pool);

        for (var i = 0; i < targets.Count && remaining > 0m; i++)
        {
            var target = targets[i];
            if (target == 0m || Math.Sign(target) != poolSign)
            {
                continue;
            }

            var allocated = Math.Min(remaining, Math.Abs(target));
            result[i] = poolSign * allocated;
            remaining -= allocated;
        }

        return result;
    }
}
