using System.Collections.ObjectModel;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using bmcs_app.Application.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bmcs_app.ViewModels.Master;

/// <summary>
/// プリンタ環境設定画面（TODO.md 2-6）。端末ローカルの bmcs_config.json に保存し、DBでは管理しない。
/// 帳票種別（納品書・請求書・明細請求書）ごとに出力先プリンタを選択する。
/// </summary>
public partial class PrinterSettingsViewModel(PrinterSettingsService printerSettingsService) : ViewModelBase
{
    private const string NoPrinterText = "（未設定）";

    public ObservableCollection<string> Printers { get; } = [NoPrinterText];

    [ObservableProperty]
    public partial string DeliverySlipPrinter { get; set; } = NoPrinterText;

    [ObservableProperty]
    public partial string InvoicePrinter { get; set; } = NoPrinterText;

    [ObservableProperty]
    public partial string LineInvoicePrinter { get; set; } = NoPrinterText;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>画面表示時にプリンタ一覧と保存済み設定を読み込む（Window の Loaded から呼ばれる）。</summary>
    [RelayCommand]
    private Task LoadAsync() => RunBusyAsync(() =>
    {
        Printers.Clear();
        Printers.Add(NoPrinterText);

        var server = new LocalPrintServer();
        var queues = server.GetPrintQueues([
            EnumeratedPrintQueueTypes.Local,
            EnumeratedPrintQueueTypes.Connections,
        ]);
        foreach (var queue in queues.OrderBy(q => q.FullName))
        {
            Printers.Add(queue.FullName);
        }

        var settings = printerSettingsService.Load();
        DeliverySlipPrinter = settings.DeliverySlipPrinter ?? NoPrinterText;
        InvoicePrinter = settings.InvoicePrinter ?? NoPrinterText;
        LineInvoicePrinter = settings.LineInvoicePrinter ?? NoPrinterText;
        StatusMessage = string.Empty;

        return Task.CompletedTask;
    });

    [RelayCommand]
    private void Save()
    {
        printerSettingsService.Save(new PrinterSettings(
            NullIfUnset(DeliverySlipPrinter),
            NullIfUnset(InvoicePrinter),
            NullIfUnset(LineInvoicePrinter)));

        StatusMessage = "保存しました。";
    }

    private static string? NullIfUnset(string value) => value == NoPrinterText ? null : value;
}
