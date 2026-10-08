using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 売上明細行の消込状態（<see cref="SettlementStatus"/>）を、対象額（<c>sales.amount</c>）と
/// 消込済金額（<c>sales.settled_amount</c>）から判定する。
/// </summary>
public static class SettlementStatusCalculator
{
    /// <param name="amount">対象額（<c>sales.amount</c>）。返品・値引行はマイナス。</param>
    /// <param name="settledAmount">
    /// 消込済金額。<see cref="SettlementAllocator"/> の配分結果であり、<paramref name="amount"/>
    /// と同符号かつ絶対値が <paramref name="amount"/> を超えないことを呼び出し元が保証する。
    /// </param>
    public static SettlementStatus Determine(decimal amount, decimal settledAmount)
    {
        // amount = 0 の行（業務上想定しないが安全側）や settledAmount = 0（未消込）は常に未消込。
        if (settledAmount == 0m || amount == 0m)
        {
            return SettlementStatus.Unsettled;
        }

        if (Math.Sign(settledAmount) == Math.Sign(amount) && Math.Abs(settledAmount) >= Math.Abs(amount))
        {
            return SettlementStatus.FullySettled;
        }

        return SettlementStatus.PartiallySettled;
    }
}
