using bmcs_app.Infrastructure.LocalSettings;

namespace bmcs_app.Application.Common;

/// <summary>
/// プリンタ設定（端末ローカル、<c>bmcs_config.json</c>）の読み書き。DBは使わない。
/// </summary>
public class PrinterSettingsService
{
    public PrinterSettings Load()
    {
        var config = PrinterSettingsConfig.Load();
        return new PrinterSettings(
            config.DeliverySlipPrinter,
            config.InvoicePrinter,
            config.LineInvoicePrinter,
            config.ReceivablesBalancePrinter);
    }

    public void Save(PrinterSettings settings)
    {
        PrinterSettingsConfig.Save(new PrinterSettingsConfig
        {
            DeliverySlipPrinter = settings.DeliverySlipPrinter,
            InvoicePrinter = settings.InvoicePrinter,
            LineInvoicePrinter = settings.LineInvoicePrinter,
            ReceivablesBalancePrinter = settings.ReceivablesBalancePrinter,
        });
    }
}
