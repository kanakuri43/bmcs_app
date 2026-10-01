namespace bmcs_app.Application.Common;

/// <summary>プリンタ設定（端末ローカル）。帳票種別ごとに出力先プリンタ名を持つ。未設定は null。</summary>
public record PrinterSettings(
    string? DeliverySlipPrinter,
    string? InvoicePrinter,
    string? LineInvoicePrinter,
    string? ReceivablesBalancePrinter);
