using bmcs_app.Domain.Enums;
using bmcs_app.Domain.Numbering;
using bmcs_app.Infrastructure.Numbering;

namespace bmcs_app.Application.Common;

/// <summary>
/// 伝票番号の採番ユースケース（TODO.md 4-1）。
/// 呼び出し元（受注入力等の各伝票登録ユースケース）が明示トランザクション
/// （<c>dbContext.Database.BeginTransactionAsync()</c>）を開始した上で、
/// 伝票の登録処理（<c>SaveChangesAsync</c>）とあわせて呼び出すこと
/// （docs/architecture.md 6章）。
/// </summary>
public class SlipNumberService(
    SlipNumberSequenceCommand sequenceCommand,
    ICurrentEmployeeContext currentEmployeeContext)
{
    /// <summary>次の伝票番号を発行する（8桁ゼロ埋め、例: <c>00000001</c>）。</summary>
    public async Task<string> NextAsync(SlipNumberKind kind, CancellationToken cancellationToken = default)
    {
        var sequenceKey = SlipNumberFormatter.ToSequenceKey(kind);
        var nextValue = await sequenceCommand.IncrementAsync(
            sequenceKey, currentEmployeeContext.EmployeeCode, cancellationToken);

        return SlipNumberFormatter.Format(nextValue);
    }
}
