using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Closing;

/// <summary>
/// 月次締め済みかどうかの判定（TODO.md 9-2）。売上・入金の編集ロック（既存行の訂正・取消）と、
/// 新規登録・日付変更の入口検証の両方がこのクラスを使う（判定ルールを1か所に集約する）。
///
/// 「得意先 C の日付 D は月次締め済み」⇔ <c>monthly_closings</c> に、<c>closing_date</c> が D の月末日、
/// <c>closing_status = Confirmed</c>、<c>customer_code</c> が C **または C の請求集約先**
/// （<c>customers.billing_customer_code</c>）である行がある。請求集約先の月次行にはグループ全体
/// （請求集約元の売上・入金）が入るため、請求集約元に自分の行が無くても、請求集約先が確定済みなら
/// 請求集約元の伝票も動かせないようにする（2026-09-30ユーザー確認）。単独得意先・請求集約先は
/// <c>billing_customer_code</c> が自分自身なので、自社の行だけを見る従来どおりの挙動になる。
/// 解除済み（<see cref="ClosingStatus.Released"/>）は締め済みに含めない。
/// </summary>
public class MonthlyClosedService(BmcsDbContext dbContext)
{
    public async Task<bool> IsClosedAsync(
        string customerCode, DateOnly date, CancellationToken cancellationToken = default)
    {
        var monthEnd = MonthlyClosingService.MonthEnd(date.Year, date.Month);

        var billingCustomerCode = await dbContext.Customers
            .AsNoTracking()
            .Where(c => c.CustomerCode == customerCode)
            .Select(c => c.BillingCustomerCode)
            .FirstOrDefaultAsync(cancellationToken) ?? customerCode;

        return await dbContext.MonthlyClosings
            .AsNoTracking()
            .AnyAsync(
                m => m.ClosingDate == monthEnd
                    && m.ClosingStatus == ClosingStatus.Confirmed
                    && (m.CustomerCode == customerCode || m.CustomerCode == billingCustomerCode),
                cancellationToken);
    }

    /// <summary>
    /// 新規登録・日付変更の入口検証。締め済みなら画面に出せるメッセージを、未締めなら null を返す。
    /// <paramref name="dateLabel"/> はメッセージに埋め込む項目名（例:「売上日付」「入金日付」）。
    /// </summary>
    public async Task<string?> CheckEntryAsync(
        string customerCode, DateOnly date, string dateLabel, CancellationToken cancellationToken = default)
        => await IsClosedAsync(customerCode, date, cancellationToken)
            ? $"{dateLabel}の年月（{date:yyyy年MM月}）は月次締め済みのため登録・変更できません。"
            : null;
}
