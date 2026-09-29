namespace bmcs_app.Application.Receipt;

/// <summary>
/// <see cref="SettlementService.RecalculateForBillingGroupAsync"/> の結果。実際に値が変わった
/// 行数を返す（変化がなければ0）。TODO.md 7-6（消込整合性レビュー）で、再計算を実行しても
/// 更新が発生しないこと（＝キャッシュ列が実態と一致していること）を検査する用途を想定する。
/// </summary>
public readonly record struct SettlementRecalculationResult(int UpdatedSalesLineCount, int UpdatedReceiptLineCount);
