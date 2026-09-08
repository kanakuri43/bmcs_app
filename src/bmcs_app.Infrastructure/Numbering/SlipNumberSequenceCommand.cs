using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Infrastructure.Numbering;

/// <summary>
/// 採番（<c>slip_number_sequence</c>）の同時実行制御付きINCREMENT（TODO.md 4-1）。
/// 単一の <c>UPDATE ... OUTPUT</c> 文で「+1して読む」をアトミックに行う。SQL Server の
/// 行ロックが直列化を保証するため、このテーブルは row_version を持たない
/// （docs/database-schema.md 2.17節）。
/// </summary>
/// <remarks>
/// <c>dbContext.Database.SqlQuery&lt;T&gt;()</c> をそのまま使う（<c>BmcsDbContext.GetServerVersionAsync</c>
/// と同じイディオム）。EF Core は LINQ で合成していないスカラー <c>SqlQuery</c>（<c>Where</c>／
/// <c>OrderBy</c>／<c>Take</c> 等を付けていないもの）は生SQLをそのまま1コマンドとして発行し、
/// 派生テーブルに包まない（<c>SelectExpression.IsNonComposedFromSql</c>、EF Core 10）。
/// そのため <c>UPDATE ... OUTPUT</c> がそのまま実行できる。実機DBでも
/// <c>BEGIN TRAN; UPDATE ... OUTPUT ...; ROLLBACK;</c> で動作確認済み。
///
/// 【重要】<c>ToListAsync()</c> で受け取ること。<c>FirstOrDefaultAsync</c>／<c>SingleAsync</c>／
/// <c>Take(1)</c> 等を使うと `TOP` が合成されて <c>SELECT ... FROM ( UPDATE ... ) AS [s]</c> に
/// 変形され、SQL Server が入れ子 UPDATE を拒否する（Msg 10729）。コンパイルは通るため、
/// この制約は気付かれにくい。
/// </remarks>
public class SlipNumberSequenceCommand(BmcsDbContext dbContext)
{
    /// <summary>
    /// <paramref name="sequenceKey"/> の現在値を +1 して、更新後の値を返す。
    /// </summary>
    /// <remarks>
    /// 呼び出し元は必ず <c>dbContext.Database.BeginTransactionAsync()</c> で開始した
    /// 明示トランザクション内から呼び出すこと。伝票登録と同一トランザクションでない場合、
    /// 登録失敗時に採番だけが確定してしまい欠番が出る（docs/architecture.md 6章）。
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// 明示トランザクションが開始されていない場合、または <paramref name="sequenceKey"/>
    /// の行が <c>slip_number_sequence</c> に存在しない場合。
    /// </exception>
    public async Task<long> IncrementAsync(
        string sequenceKey, string employeeCode, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "伝票番号の採番は、伝票登録と同一の明示トランザクション内で実行してください" +
                "（dbContext.Database.BeginTransactionAsync() を先に呼び出す）。" +
                "docs/architecture.md 6章「伝票番号の採番は、伝票登録と同一トランザクション内で行う」を参照。");
        }

        // CAST(... AS varchar(n)): 補間文字列パラメータは既定で nvarchar で送られるため、
        // 明示しないと列側（varchar）が暗黙変換されてインデックスシークを維持できない
        // （docs/architecture.md 10章「varchar/char列はIsUnicode(false)を明示する」と同種の理由）。
        var values = await dbContext.Database
            .SqlQuery<long>($"""
                UPDATE dbo.slip_number_sequence
                SET current_value = current_value + 1,
                    updated_by    = CAST({employeeCode} AS varchar(10)),
                    updated_at    = SYSDATETIME()
                OUTPUT inserted.current_value AS [Value]
                WHERE sequence_key = CAST({sequenceKey} AS varchar(30))
                """)
            .ToListAsync(cancellationToken);

        return values.Count == 1
            ? values[0]
            : throw new InvalidOperationException(
                $"採番キー '{sequenceKey}' の行が slip_number_sequence に存在しません。");
    }
}
