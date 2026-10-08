namespace bmcs_app.Domain.Enums;

/// <summary>
/// 採番系列の種別。<c>slip_number_sequences.sequence_key</c> に対応する。
/// 税単位（<see cref="TaxUnit"/>）では系列を分けない。伝票番号が得意先の税区分によって
/// 別系列になると、現場で伝票番号から伝票を探すときに混乱するため
/// （docs/database-schema.md 2.17節）。
/// </summary>
public enum SlipNumberKind
{
    /// <summary>受注（<c>order_slip</c>）。</summary>
    OrderSlip,

    /// <summary>売上（<c>sales_slip</c>）。税単位（請求/伝票/内税明細）を問わず1系列。</summary>
    SalesSlip,

    /// <summary>締め入金（<c>receipt_slip</c>）。</summary>
    ReceiptSlip,

    /// <summary>明細入金（<c>detail_receipt</c>）。</summary>
    DetailReceipt,

    /// <summary>請求データ（<c>billing</c>）。</summary>
    Billing,

    /// <summary>明細請求書（<c>detail_invoice</c>）。</summary>
    DetailInvoice,
}
