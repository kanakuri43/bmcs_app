namespace bmcs_app.Domain.Entities;

/// <summary>
/// 明細請求書と売上明細行の連携（detail_invoice_sales_line）。
/// UNIQUE (SalesSlipNumber, SalesLineNumber) により、1つの売上明細行が紐づく
/// 明細請求書は最大1つに制限される（二重請求防止。DB側で強制済み）。
/// 行の追加・削除のみで更新がないため RowVersion を持たない（TrackedEntity を継承）。
/// </summary>
public class DetailInvoiceSalesLine : TrackedEntity
{
    public required string DetailInvoiceNumber { get; set; }

    public required string SalesSlipNumber { get; set; }

    public required short SalesLineNumber { get; set; }
}
