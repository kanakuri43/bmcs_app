using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface ISaleRepository
{
    /// <summary>伝票一覧（ヘッダー情報のみ）を取得する</summary>
    Task<IEnumerable<SlipSummary>> GetSummariesAsync();

    /// <summary>伝票Noで1件取得する。存在しない場合は null を返す</summary>
    Task<SaleSlip?> GetBySlipNoAsync(string saleNo);

    /// <summary>売上伝票を登録・更新する（usp_sales_upsert）</summary>
    Task UpsertAsync(
        string saleNo, DateOnly saleDate, int customerId,
        int? orderId, string? orderNo, int employeeId,
        string? slipRemarks, IEnumerable<SaleLineInput> lines);

    /// <summary>売上伝票を論理削除する（usp_sales_delete）</summary>
    Task DeleteAsync(string saleNo);
}
