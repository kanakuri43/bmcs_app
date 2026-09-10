using bmcs_app.Domain.Entities;

namespace bmcs_app.Application.Common;

/// <summary>
/// 伝票単位の楽観的排他制御の共通処理（docs/architecture.md 9章、TODO.md 5-6で実装）。
/// rowversion は明細行単位（物理的な適用単位）だが、業務上の編集単位は「伝票」であるため、
/// 行単位の rowversion だけでは次の2つを検出できない。
/// 1. 自分が変更しなかった明細行を、他のユーザーが変更した
/// 2. 他のユーザーが同じ伝票に明細行を追加・削除した
/// 伝票種別ごとに書かず、ここに1箇所だけ実装する。
/// </summary>
public static class SlipConcurrencyGuard
{
    /// <summary>
    /// 保存直前に明細行の集合を再取得し、読込時点と一致することを確認する（上記2の検出）。
    /// 一致しなければ他のユーザーが行を追加・削除したと判断し、更新を中止する。
    /// </summary>
    /// <exception cref="SlipConcurrencyException">読込時点と現在の明細行の集合が一致しない場合。</exception>
    public static void EnsureLineSetUnchanged<TKey>(
        IReadOnlyCollection<TKey> loadedKeys, IReadOnlyCollection<TKey> currentKeys)
    {
        if (loadedKeys.Count != currentKeys.Count || !loadedKeys.ToHashSet().SetEquals(currentKeys))
        {
            throw new SlipConcurrencyException("他のユーザーが更新しました。再読み込みしてください。");
        }
    }

    /// <summary>
    /// 読み込んだ全明細行を更新対象に含め、値を変えていない行も rowversion の照合を受けさせる
    /// （上記1の検出）。監査列を更新するだけで EF Core が当該行を Modified としてマークし、
    /// SaveChanges 時に元の rowversion を WHERE 句に含めた UPDATE を発行するようになる。
    /// </summary>
    public static void TouchAll<TEntity>(IEnumerable<TEntity> lines, string employeeCode, DateTime now)
        where TEntity : AuditableEntity
    {
        foreach (var line in lines)
        {
            line.UpdatedBy = employeeCode;
            line.UpdatedAt = now;
        }
    }
}

/// <summary>
/// 伝票単位の楽観的排他制御の競合（他のユーザーによる更新・行の追加削除）。
/// 自動マージ・後勝ちでの上書きはしない（docs/architecture.md 9章）。
/// </summary>
public sealed class SlipConcurrencyException(string message, Exception? inner = null) : Exception(message, inner);
