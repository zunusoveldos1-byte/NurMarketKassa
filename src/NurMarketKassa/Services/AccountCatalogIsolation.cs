using System.Windows;
using NurMarketKassa.Views;

namespace NurMarketKassa.Services;

/// <summary>
/// Изоляция локального каталога при смене учётной записи кассира.
/// </summary>
public static class AccountCatalogIsolation
{
    public static bool RequireForcedCatalogSync { get; private set; }

    /// <summary>
    /// Вызывать после успешной авторизации, до открытия главного окна.
    /// </summary>
    public static void PrepareForAuthenticatedUser(string email, string? userId)
    {
        var key = BuildUserKey(email, userId);
        var previous = UserPreferences.Instance.LastCatalogUserKey;

        if (!string.IsNullOrEmpty(previous)
            && string.Equals(previous, key, StringComparison.OrdinalIgnoreCase))
        {
            RequireForcedCatalogSync = false;
            return;
        }

        PosLogger.Log(
            $"Смена пользователя: «{previous ?? "—"}» → «{key}». Очистка локального каталога.",
            "AUTH");

        ClearLocalCatalogData();
        UserPreferences.Instance.LastCatalogUserKey = key;
        UserPreferences.Instance.SaveToDisk();
        RequireForcedCatalogSync = true;
    }

    public static void ClearForcedCatalogSyncFlag() => RequireForcedCatalogSync = false;

    public static void ClearLocalCatalogData()
    {
        LocalProductRepository.Instance.ClearAll();
        CatalogCacheService.ClearInMemory();

        if (Application.Current?.MainWindow is MainWindow mainWindow)
            mainWindow.ClearCatalogViewport();
    }

    private static string BuildUserKey(string email, string? userId)
    {
        var mail = (email ?? "").Trim().ToLowerInvariant();
        var id = (userId ?? "").Trim();
        return string.IsNullOrEmpty(id) ? mail : $"{mail}|{id}";
    }
}
