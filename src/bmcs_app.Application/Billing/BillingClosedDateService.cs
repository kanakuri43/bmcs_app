using bmcs_app.Domain.Calculations;
using bmcs_app.Domain.Enums;
using bmcs_app.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 請求締め済み期間への売上・入金の新規登録／日付変更を防ぐ入口バリデーション（申し送り事項R2の解消。
/// docs/design_document.md 21-4章）。判定自体は<see cref="BillingClosedDateEvaluator"/>（Domain純粋関数）
/// に委ね、本クラスはDBアクセス（対象得意先の確定済み<c>billing</c>のうち最新の<c>billing_date</c>の照会）
/// のみ担う。都度得意先（<see cref="TaxUnit.Line"/>）は<c>billing</c>を1件も持たないため、税単位で
/// 分岐せずとも自動的に無制限になる。
/// </summary>
public class BillingClosedDateService(BmcsDbContext dbContext)
{
    public Task<DateOnly?> GetLatestConfirmedBillingDateAsync(
        string customerCode, CancellationToken cancellationToken = default)
        => dbContext.Billings
            .AsNoTracking()
            .Where(b => b.CustomerCode == customerCode && !b.IsDeleted && b.BillingStatus == BillingStatus.Confirmed)
            .Select(b => (DateOnly?)b.BillingDate)
            .MaxAsync(cancellationToken);

    /// <summary>画面の<c>DatePicker.DisplayDateStart</c>供給用。制限が無ければ<c>null</c>。</summary>
    public async Task<DateOnly?> GetMinimumEntryDateAsync(
        string customerCode, CancellationToken cancellationToken = default)
        => BillingClosedDateEvaluator.MinimumEntryDate(
            await GetLatestConfirmedBillingDateAsync(customerCode, cancellationToken));

    /// <summary><paramref name="dateLabel"/>はメッセージに埋め込む項目名（例:「売上日付」「入金日付」）。</summary>
    public async Task<BillingClosedDateCheck> CheckAsync(
        string customerCode, DateOnly slipDate, string dateLabel, CancellationToken cancellationToken = default)
        => BillingClosedDateEvaluator.Check(
            slipDate,
            await GetLatestConfirmedBillingDateAsync(customerCode, cancellationToken),
            dateLabel);
}
