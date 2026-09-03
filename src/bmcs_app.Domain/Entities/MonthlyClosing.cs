using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 月次締め（monthly_closing）。全社単位で月次に1レコード（得意先ごとではない）。
/// 「未締め」はレコードが存在しない状態で表す。
/// 編集ロックは伝票側にフラグを持たず、伝票日付の年月と本テーブルを突き合わせて導出する。
/// </summary>
public class MonthlyClosing : AuditableEntity
{
    /// <summary>対象年月（YYYYMM）。</summary>
    public required string ClosingYearMonth { get; set; }

    public required ClosingStatus ClosingStatus { get; set; }

    public required DateTime ConfirmedAt { get; set; }

    public required string ConfirmedBy { get; set; }

    public DateTime? ReleasedAt { get; set; }

    public string? ReleasedBy { get; set; }
}
