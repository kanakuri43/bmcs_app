using System.Text.Json;

namespace bmcs_app.Infrastructure;

public static class AppConfig
{
    private static readonly Lazy<string> _connectionString = new(Load);

    public static string ConnectionString => _connectionString.Value;

    private static string Load()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bmcs_config.json");

        if (!File.Exists(path))
            throw new FileNotFoundException($"設定ファイルが見つかりません: {path}");

        using var stream = File.OpenRead(path);
        var doc = JsonDocument.Parse(stream);

        if (!doc.RootElement.TryGetProperty("connectionString", out var prop))
            throw new InvalidOperationException("config.json に connectionString が定義されていません");

        return prop.GetString()
            ?? throw new InvalidOperationException("config.json の connectionString が空です");
    }
}
