namespace bmcs_app.Domain.Entities;

/// <summary>
/// 税率マスタ（tax_rate_master）。施行日付きで税率を管理する。
/// 適用期間は「EffectiveDate 〜 次に新しい EffectiveDate を持つレコードの前日まで
/// （最新レコードは無期限）」で決まるため、終了日カラムは持たない。
/// 伝票登録時は対象の伝票日付以前で最も新しい EffectiveDate のレコードを採用し、
/// TaxCategory に応じて StandardTaxRate / ReducedTaxRate のいずれかを転記する。
/// 非課税（TaxCategory.TaxExempt）は本マスタを参照しない。
/// </summary>
public class TaxRateMaster : AuditableEntity
{
    public required DateOnly EffectiveDate { get; set; }

    /// <summary>標準税率（TaxCategory.Standard）。</summary>
    public required decimal StandardTaxRate { get; set; }

    /// <summary>軽減税率（TaxCategory.Reduced）。</summary>
    public required decimal ReducedTaxRate { get; set; }
}
