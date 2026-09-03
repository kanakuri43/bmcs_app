namespace bmcs_app.Domain.Enums;

/// <summary>明細入金の充当先種別（detail_payment.target_type）。</summary>
public enum DetailPaymentTargetType : byte
{
    /// <summary>売上明細行を直接指定。</summary>
    SalesLine = 1,

    /// <summary>明細請求書を指定。</summary>
    DetailInvoice = 2,
}
