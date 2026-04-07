using System.Text.Json;
using System.Text.Json.Serialization;

namespace bmcs_app.Infrastructure;

/// <summary>プリンタ設定（bmcs_printer_settings.json）の読み書き</summary>
public class PrinterSettingsConfig
{
    [JsonPropertyName("deliverySlipPrinter")]
    public string? DeliverySlipPrinter { get; set; }

    [JsonPropertyName("invoicePrinter")]
    public string? InvoicePrinter { get; set; }

    private static string FilePath
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bmcs_printer_settings.json");

    public static PrinterSettingsConfig Load()
    {
        var path = FilePath;
        if (!File.Exists(path)) return new();
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<PrinterSettingsConfig>(stream) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void Save(PrinterSettingsConfig config)
    {
        var json = JsonSerializer.Serialize(config,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
