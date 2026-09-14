using bmcs_app.Domain.Enums;

namespace bmcs_app.Application.Billing;

/// <summary>
/// 伝票検索モーダル（<c>SlipSearchDialogViewModel</c>）の明細請求書検索結果1件。
/// <c>detail_invoice</c> はヘッダー1テーブル構成のため、<see cref="Application.Sales.SalesSlipHit"/> と
/// 異なり伝票単位への集約は不要。
/// </summary>
public sealed record DetailInvoiceHit(
    string DetailInvoiceNumber,
    DateOnly IssueDate,
    string CustomerCode,
    string CustomerName,
    string AddresseeName,
    decimal TotalAmount,
    DetailInvoiceStatus InvoiceStatus);
