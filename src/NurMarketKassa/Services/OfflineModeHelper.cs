namespace NurMarketKassa.Services;

public static class OfflineModeHelper
{
    public static bool IsNetworkOnline => App.OfflineSync?.IsOnline == true;

    public static bool UseLocalOperations => App.OfflineSync != null && !App.OfflineSync.IsOnline;

    public static bool CanOperateWithoutServer => App.IsOfflineBootstrap || UseLocalOperations;
}
