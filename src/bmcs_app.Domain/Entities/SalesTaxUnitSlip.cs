namespace bmcs_app.Domain.Entities;

/// <summary>売上（伝票単位の得意先）。伝票登録時に伝票単位で税額を確定する。</summary>
public class SalesTaxUnitSlip : SalesTaxUnitBase
{
    /// <summary>伝票単位の税額。同一伝票の全行に同値（SUM してはいけない）。</summary>
    public required decimal SlipTaxAmount { get; set; }

    /// <summary>請求データへの参照。NULL＝未請求。</summary>
    public string? BillingNumber { get; set; }
}
