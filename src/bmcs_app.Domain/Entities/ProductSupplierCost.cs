namespace bmcs_app.Domain.Entities;

/// <summary>
/// 商品×仕入先の原価（product_supplier_costs）。伝票明細の仕入先に有効な行があれば、
/// その原価を明細の原価へ転記する（なければ商品マスタの標準原価）。
/// </summary>
public class ProductSupplierCost : AuditableEntity
{
    public required string ProductCode { get; set; }

    public required string SupplierCode { get; set; }

    public decimal CostPrice { get; set; }
}
