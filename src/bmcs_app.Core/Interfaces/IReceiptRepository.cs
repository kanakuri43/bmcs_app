using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface IReceiptRepository
{
    /// <summary>伝票一覧（ヘッダー情報のみ）を取得する</summary>
    Task<IEnumerable<SlipSummary>> GetSummariesAsync();

    /// <summary>伝票Noで1件取得する。存在しない場合は null を返す</summary>
    Task<ReceiptSlip?> GetByReceiptNoAsync(string receiptNo);

    /// <summary>入金伝票を登録・更新する（usp_receipts_upsert）</summary>
    Task UpsertAsync(
        string receiptNo, DateOnly receiptDate, int customerId,
        string? slipRemarks, IEnumerable<ReceiptLineInput> lines);

    /// <summary>入金伝票を論理削除する（usp_receipts_delete）</summary>
    Task DeleteAsync(string receiptNo);
}
