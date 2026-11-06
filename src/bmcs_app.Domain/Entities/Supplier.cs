namespace bmcs_app.Domain.Entities;

/// <summary>
/// 仕入先マスタ（suppliers）。商品の主仕入先（<see cref="Product.MainSupplierCode"/>）、
/// 商品×仕入先の原価（<see cref="ProductSupplierCost"/>）、伝票明細の仕入先として参照される。
/// </summary>
public class Supplier : AuditableEntity
{
    public required string SupplierCode { get; set; }

    public required string SupplierName { get; set; }

    public string? SupplierNameKana { get; set; }

    public string? PostalCode { get; set; }

    public string? Address1 { get; set; }

    public string? Address2 { get; set; }

    public string? PhoneNumber { get; set; }

    public string? FaxNumber { get; set; }

    /// <summary>仕入先側の担当者名。</summary>
    public string? ContactPersonName { get; set; }

    public string? Remarks { get; set; }
}
