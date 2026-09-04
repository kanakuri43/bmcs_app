namespace bmcs_app.Domain.Entities;

/// <summary>
/// 売上（請求単位の得意先）。
/// 請求締め時に一括計算するため、伝票時点では税額カラムを持たない。
/// </summary>
public class SalesTaxUnitInvoice : SalesTaxUnitBase
{
    /// <summary>請求データへの参照。NULL＝未請求。</summary>
    public string? BillingNumber { get; set; }
}
