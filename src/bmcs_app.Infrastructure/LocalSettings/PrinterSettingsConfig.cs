using System.Text.Json;
using System.Text.Json.Serialization;

namespace bmcs_app.Infrastructure.LocalSettings;

/// <summary>
/// 端末ローカルのプリンタ設定（<c>bmcs_config.json</c>）の読み書き。DBでは管理しない（TODO.md 2-6）。
/// 接続文字列など他の設定は appsettings.json 側で管理しており、このファイルはプリンタ設定専用。
/// </summary>
public class PrinterSettingsConfig
{
    [JsonPropertyName("deliverySlipPrinter")]
    public string? DeliverySlipPrinter { get; set; }

    [JsonPropertyName("invoicePrinter")]
    public string? InvoicePrinter { get; set; }

    [JsonPropertyName("lineInvoicePrinter")]
    public string? LineInvoicePrinter { get; set; }

    [JsonPropertyName("receivablesBalancePrinter")]
    public string? ReceivablesBalancePrinter { get; set; }

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "bmcs_config.json");

    public static PrinterSettingsConfig Load()
    {
        var path = FilePath;
        if (!File.Exists(path))
        {
            return new PrinterSettingsConfig();
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<PrinterSettingsConfig>(stream) ?? new PrinterSettingsConfig();
    }

    public static void Save(PrinterSettingsConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
