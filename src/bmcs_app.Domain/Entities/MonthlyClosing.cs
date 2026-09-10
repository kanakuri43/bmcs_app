using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>
/// 月次締め（monthly_closing）。得意先×月末日で1レコード（billing 類似レイアウト）。
/// billing は得意先ごとの締め日（closing_day）期間で集計するが、会計上の月次売掛金は
/// 全得意先を暦月（月初〜月末）で集計する必要があるため、別々の締め処理として併存する。
/// 「未締め」はレコードが存在しない状態で表す。
/// 編集ロックは、伝票側にフラグを持たず、CustomerCode＋伝票日付の年月と本テーブルの
/// 突き合わせ、または billing への集計済みかどうかで導出する。
/// </summary>
public class MonthlyClosing : AuditableEntity
{
    /// <summary>対象月の月末日。</summary>
    public required DateOnly ClosingDate { get; set; }

    public required string CustomerCode { get; set; }

    /// <summary>集計時点の得意先税区分のスナップショット。</summary>
    public required TaxUnit TaxUnit { get; set; }

    public required string CustomerName { get; set; }

    public required decimal PreviousBalance { get; set; }

    public required decimal SalesAmount { get; set; }

    public required decimal ReceiptAmount { get; set; }

    /// <summary>
    /// 消費税額。TaxUnit.Invoice の得意先で、まだ請求締めを通っていない区間（暦月末が
    /// 得意先自身の締め期間の途中にある場合）は、確定させずに仮計算した値を保持する。
    /// </summary>
    public required decimal TaxAmount { get; set; }

    /// <summary>当月末売掛残高（PreviousBalance + SalesAmount + TaxAmount - ReceiptAmount）。</summary>
    public required decimal ClosingBalance { get; set; }

    /// <summary>税率別内訳: 標準税率の対価額。</summary>
    public required decimal StandardRateTaxableAmount { get; set; }

    public required decimal StandardRateTaxAmount { get; set; }

    /// <summary>軽減税率の対価額。</summary>
    public required decimal ReducedRateTaxableAmount { get; set; }

    public required decimal ReducedRateTaxAmount { get; set; }

    public required decimal TaxExemptAmount { get; set; }

    public required ClosingStatus ClosingStatus { get; set; }

    public required DateTime ConfirmedAt { get; set; }

    public required string ConfirmedBy { get; set; }

    public DateTime? ReleasedAt { get; set; }

    public string? ReleasedBy { get; set; }
}
