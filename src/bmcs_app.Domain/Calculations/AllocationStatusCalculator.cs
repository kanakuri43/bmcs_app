using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Calculations;

/// <summary>
/// 入金伝票の充当状態（<see cref="AllocationStatus"/>）を、伝票単位の入金額
/// （<c>receipt_amount</c>）と同一伝票の行の充当額合計から判定する（TODO.md 7-1）。
/// </summary>
public static class AllocationStatusCalculator
{
    /// <param name="receiptAmount">伝票単位の入金額（<c>receipt_amount</c>。SUMしてはいけない値）。</param>
    /// <param name="allocatedTotal">
    /// 同一伝票の全行の <c>allocated_amount</c> 合計。振込手数料差額（<c>fee_adjustment_amount</c>）
    /// は実際に受け取った金額ではないため含めない。
    /// </param>
    public static AllocationStatus Determine(decimal receiptAmount, decimal allocatedTotal)
    {
        // allocatedTotal = 0 は前受・過入金（充当先未定）を含む。常に未充当。
        if (allocatedTotal == 0m || receiptAmount == 0m)
        {
            return AllocationStatus.Unallocated;
        }

        if (Math.Sign(allocatedTotal) == Math.Sign(receiptAmount) && Math.Abs(allocatedTotal) >= Math.Abs(receiptAmount))
        {
            return AllocationStatus.FullyAllocated;
        }

        return AllocationStatus.PartiallyAllocated;
    }
}
