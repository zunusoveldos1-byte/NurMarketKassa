using System.IO;
using System.Text.Json;

namespace NurMarketKassa.Configuration;

/// <summary>
/// Базовые настройки. Перекрытие: переменная окружения DESKTOP_MARKET_API_URL (как в Python config.py).
/// </summary>
public sealed class AppSettings
{
    public string ApiBaseUrl { get; init; } = "https://app.nurcrm.kg";

    public ReceiptPrinterSettings ReceiptPrinter { get; init; } = new();

    public ScaleSettings Scale { get; init; } = new();

    public CatalogUiSettings Catalog { get; init; } = new();

    public UpdateSettings Updates { get; init; } = new();

    public static AppSettings Load()
    {
        var env = Environment.GetEnvironmentVariable("DESKTOP_MARKET_API_URL")?.Trim();
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        AppSettings? fromFile = null;
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                fromFile = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            }
            catch
            {
                /* ignore */
            }
        }

        var merged = fromFile ?? new AppSettings();
        merged = CoalesceSections(merged);

        if (!string.IsNullOrWhiteSpace(env))
        {
            merged = new AppSettings
            {
                ApiBaseUrl = env.TrimEnd('/'),
                ReceiptPrinter = merged.ReceiptPrinter,
                Scale = merged.Scale,
                Catalog = merged.Catalog,
                Updates = merged.Updates,
            };
        }

        var manifestEnv = Environment.GetEnvironmentVariable("DESKTOP_MARKET_UPDATE_MANIFEST_URL")?.Trim();
        if (!string.IsNullOrWhiteSpace(manifestEnv))
        {
            merged = new AppSettings
            {
                ApiBaseUrl = merged.ApiBaseUrl,
                ReceiptPrinter = merged.ReceiptPrinter,
                Scale = merged.Scale,
                Catalog = merged.Catalog,
                Updates = new UpdateSettings
                {
                    ManifestUrl = manifestEnv,
                    CheckOnStartup = merged.Updates.CheckOnStartup,
                    MinHoursBetweenChecks = merged.Updates.MinHoursBetweenChecks,
                },
            };
        }

        return merged;
    }

    private static AppSettings CoalesceSections(AppSettings s) =>
        new()
        {
            ApiBaseUrl = s.ApiBaseUrl,
            ReceiptPrinter = s.ReceiptPrinter ?? new ReceiptPrinterSettings(),
            Scale = s.Scale ?? new ScaleSettings(),
            Catalog = s.Catalog ?? new CatalogUiSettings(),
            Updates = s.Updates ?? new UpdateSettings(),
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
