using System.Printing;
using bmcs_app.Infrastructure;
using Prism.Commands;
using Prism.Mvvm;

namespace bmcs_app.ViewModels;

public class PrinterSettingsViewModel : BindableBase
{
    private const string NoPrinterText = "（未設定）";

    public List<string> Printers { get; } = new();

    private string _deliverySlipPrinter = NoPrinterText;
    public string DeliverySlipPrinter
    {
        get => _deliverySlipPrinter;
        set => SetProperty(ref _deliverySlipPrinter, value);
    }

    private string _invoicePrinter = NoPrinterText;
    public string InvoicePrinter
    {
        get => _invoicePrinter;
        set => SetProperty(ref _invoicePrinter, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public DelegateCommand SaveCommand { get; }

    public PrinterSettingsViewModel()
    {
        Printers.Add(NoPrinterText);
        var server = new LocalPrintServer();
        var queues = server.GetPrintQueues(new[]
        {
            EnumeratedPrintQueueTypes.Local,
            EnumeratedPrintQueueTypes.Connections,
        });
        foreach (var q in queues.OrderBy(q => q.FullName))
            Printers.Add(q.FullName);

        var config = PrinterSettingsConfig.Load();
        _deliverySlipPrinter = string.IsNullOrWhiteSpace(config.DeliverySlipPrinter)
            ? NoPrinterText : config.DeliverySlipPrinter;
        _invoicePrinter = string.IsNullOrWhiteSpace(config.InvoicePrinter)
            ? NoPrinterText : config.InvoicePrinter;

        SaveCommand = new DelegateCommand(OnSave);
    }

    private void OnSave()
    {
        PrinterSettingsConfig.Save(new PrinterSettingsConfig
        {
            DeliverySlipPrinter = DeliverySlipPrinter == NoPrinterText ? null : DeliverySlipPrinter,
            InvoicePrinter      = InvoicePrinter      == NoPrinterText ? null : InvoicePrinter,
        });
        StatusMessage = "保存しました。";
    }
}
