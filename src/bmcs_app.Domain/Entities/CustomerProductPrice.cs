namespace bmcs_app.Domain.Entities;

/// <summary>
/// 得意先／商品／単価マスタ（customer_product_prices）。得意先の税区分に従う単価
/// （締め得意先は外税単価、都度得意先は内税単価）を、伝票明細の単価へそのまま転記する。
/// </summary>
public class CustomerProductPrice : AuditableEntity
{
    public required string CustomerCode { get; set; }

    public required string ProductCode { get; set; }

    public decimal UnitPrice { get; set; }
}
