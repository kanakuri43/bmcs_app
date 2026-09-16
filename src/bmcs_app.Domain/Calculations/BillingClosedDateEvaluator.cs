namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 請求締め済み期間への売上・入金の新規登録／日付変更を防ぐ判定（申し送り事項R2の解消。
/// docs/design_document.md 21-4章）。
///
/// 請求締め（<see cref="Entities.Billing"/>）は確定時点の売上・入金合計を
/// <see cref="Entities.Billing.CurrentBillingAmount"/> へスナップショットとして焼き込み、以後
/// 誰も再計算しない。締め後に過去日付の売上・入金を新規登録すると、この確定値と実データが
/// 永久に食い違うため、対象得意先の確定済み請求のうち最新の<see cref="Entities.Billing.BillingDate"/>
/// 以前（当日を含む）の日付では新規登録・日付変更ができないようにする。
///
/// 既存の編集ロック（<see cref="SalesEditLockEvaluator"/>／
/// <c>ReceiptEntryService.EvaluateEditLockAsync</c>）と参照する境界（最新の確定済み<c>billing_date</c>）は
/// 同じだが、対象が「既存行の編集・訂正」ではなく「新規登録・日付変更の入口」である点が異なるため、
/// 判定結果を共有せず別クラスとする。
/// </summary>
public static class BillingClosedDateEvaluator
{
    /// <summary>
    /// 登録可能な最小日付。<paramref name="latestConfirmedBillingDate"/>（対象得意先の確定済み
    /// <c>billing</c>のうち最新の<c>billing_date</c>）が<c>null</c>（確定済み請求が無い）なら
    /// <c>null</c>（制限なし）を返す。
    /// </summary>
    public static DateOnly? MinimumEntryDate(DateOnly? latestConfirmedBillingDate)
        => latestConfirmedBillingDate?.AddDays(1);

    /// <summary><paramref name="dateLabel"/>はメッセージに埋め込む項目名（例:「売上日付」「入金日付」）。</summary>
    public static BillingClosedDateCheck Check(
        DateOnly slipDate, DateOnly? latestConfirmedBillingDate, string dateLabel)
    {
        var minimumDate = MinimumEntryDate(latestConfirmedBillingDate);
        if (minimumDate is null || slipDate >= minimumDate.Value)
        {
            return new BillingClosedDateCheck(true, minimumDate, null);
        }

        return new BillingClosedDateCheck(
            false,
            minimumDate,
            $"{latestConfirmedBillingDate:yyyy/MM/dd}で請求締め済みのため、{dateLabel}は{minimumDate:yyyy/MM/dd}以降を指定してください。");
    }
}

/// <summary>
/// 日付制限の判定結果。<see cref="MinimumDate"/>は<see cref="IsAllowed"/>に関わらず、画面の
/// <c>DisplayDateStart</c>供給用に常に設定される（制限なしなら<c>null</c>）。
/// </summary>
public readonly record struct BillingClosedDateCheck(bool IsAllowed, DateOnly? MinimumDate, string? Reason);
