using bmcs_app.Core.Models;

namespace bmcs_app.Core.Interfaces;

public interface ISaleRepository
{
    /// <summary>伝票一覧（ヘッダー情報のみ）を取得する</summary>
    Task<IEnumerable<SlipSummary>> GetSummariesAsync();
}
