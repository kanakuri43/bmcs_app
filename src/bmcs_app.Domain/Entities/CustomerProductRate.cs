namespace bmcs_app.Domain.Entities;

/// <summary>
/// 得意先／商品／定価掛率マスタ（customer_product_rates）。商品マスタの定価に掛ける率を
/// 整数のパーセント（0〜100）で持つ。
/// </summary>
public class CustomerProductRate : AuditableEntity
{
    public required string CustomerCode { get; set; }

    public required string ProductCode { get; set; }

    public int CatalogPriceRate { get; set; }
}
