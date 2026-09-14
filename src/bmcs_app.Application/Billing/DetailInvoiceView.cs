using bmcs_app.Domain.Entities;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 発行済み明細請求書の読み取り専用表示用（TODO.md 6-3）。<c>detail_invoice</c>は明細行を
/// 持たないヘッダー1テーブルのため、<see cref="Lines"/>は<c>detail_invoice_sales_line</c>経由で
/// 売上ジャーナルから組み立てたものを保持する。
/// </summary>
public sealed record DetailInvoiceView(DetailInvoice Header, IReadOnlyList<DetailInvoiceSalesLineItem> Lines);
