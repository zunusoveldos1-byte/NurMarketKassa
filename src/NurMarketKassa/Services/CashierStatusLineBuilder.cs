using System.Globalization;
using NurMarketKassa.Configuration;
using NurMarketKassa.Services.Hardware;

namespace NurMarketKassa.Services;

public sealed record CashierStatusLine(string Text, string? ToolTip);

/// <summary>Понятная кассиру строка состояния: весы, принтер, каталог.</summary>
public static class CashierStatusLineBuilder
{
    public static CashierStatusLine Build(IWeightScaleService? scale = null)
    {
        var scaleText = BuildScaleStatus(scale);
        var printerText = BuildPrinterStatus();
        var (catalogText, catalogTooltip) = BuildCatalogStatus();

        return new CashierStatusLine(
            $"{scaleText} | {printerText} | {catalogText}",
            catalogTooltip);
    }

    public static string BuildScaleStatus(IWeightScaleService? scale = null)
    {
        if (HardwareModeHelper.UseVirtualScale(App.Settings))
            return "Весы: ✅ Подключены";

        if (!HardwareModeHelper.UsePhysicalScale())
        {
            PosLogger.Log("Весы: не настроены (выключены или COM не указан).", "STATUS");
            return "Весы: ❌ Не подключены";
        }

        var prefs = UserPreferences.Instance;
        var port = HardwarePortHelper.NormalizeComPort(prefs.ScaleComPort);
        var probe = ScaleReaderService.ProbePort(port);
        if (!probe.IsAvailable)
        {
            PosLogger.Log($"Весы: порт {port} недоступен — {probe.Message}", "STATUS");
            return "Весы: ❌ Не подключены";
        }

        if (scale is ComWeightScaleService comScale && !comScale.IsAvailable)
            PosLogger.Log($"Весы: порт {port} найден, фоновое чтение не запущено.", "STATUS");

        return $"Весы: ✅ Подключены ({port})";
    }

    public static string BuildPrinterStatus()
    {
        if (HardwareModeHelper.UseVirtualPrinter(App.Settings))
            return "Чековый принтер: ✅ Подключен";

        if (!HardwareModeHelper.UsePhysicalPrinter())
        {
            PosLogger.Log("Принтер: не настроен (выключен или порт не указан).", "STATUS");
            return "Чековый принтер: ❌ Не подключен";
        }

        var prefs = UserPreferences.Instance;
        var probe = PrinterPortService.ProbePort(prefs.ReceiptDevicePath);
        if (!probe.IsAvailable)
        {
            PosLogger.Log($"Принтер: порт недоступен — {probe.Message}", "STATUS");
            return "Чековый принтер: ❌ Не подключен";
        }

        return "Чековый принтер: ✅ Подключен";
    }

    public static (string Text, string? ToolTip) BuildCatalogStatus()
    {
        var time = CatalogCacheService.LastSyncTime;
        var timeText = time.HasValue
            ? time.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
            : "не обновлялся";

        var version = TryExtractCatalogVersionNumber(CatalogCacheService.LocalCatalogVersionToken);
        var tooltip = version != null ? $"Версия каталога: {version}" : null;

        return ($"Каталог обновлен: {timeText}", tooltip);
    }

    public static string FormatCatalogDiagnostics()
    {
        var time = CatalogCacheService.LastSyncTime;
        var token = CatalogCacheService.LocalCatalogVersionToken;
        var timeText = time.HasValue
            ? time.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
            : "не синхронизирована";
        var versionPart = string.IsNullOrWhiteSpace(token) ? "—" : $"v{token}";
        return $"БД: {timeText} · {versionPart}";
    }

    public static string? TryExtractCatalogVersionNumber(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var trimmed = token.Trim();
        var pipe = trimmed.IndexOf('|');
        var first = pipe > 0 ? trimmed[..pipe] : trimmed;
        return string.IsNullOrWhiteSpace(first) ? null : first.Trim();
    }
}
