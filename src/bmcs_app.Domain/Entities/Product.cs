using bmcs_app.Domain.Enums;

namespace bmcs_app.Domain.Entities;

/// <summary>商品マスタ（product）。社内商品コードを使用し、JANコードではない。</summary>
public class Product : AuditableEntity
{
    public required string ProductCode { get; set; }

    public required string ProductName { get; set; }

    public string? ProductNameKana { get; set; }

    public string? Specification { get; set; }

    public string? UnitName { get; set; }

    /// <summary>外税単価（税抜）。得意先の税区分が外税一括／外税伝票単位の場合、伝票入力時の単価初期値として転記する。</summary>
    public required decimal StandardUnitPriceExclTax { get; set; }

    /// <summary>内税単価（税込）。得意先の税区分が内税明細単位の場合、伝票入力時の単価初期値として転記する。</summary>
    public required decimal StandardUnitPriceInclTax { get; set; }

    /// <summary>標準原価。粗利計算用に伝票明細へ転記する。</summary>
    public required decimal StandardCostPrice { get; set; }

    public required TaxCategory TaxCategory { get; set; }
}
