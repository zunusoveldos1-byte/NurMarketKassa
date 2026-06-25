namespace NurMarketKassa.Services;

public sealed class CatalogSyncResult
{
    public bool Success { get; init; }

    public int Added { get; init; }

    public int Changed { get; init; }

    public int Deleted { get; init; }

    public string? ErrorMessage { get; init; }

    public CatalogVersionInfo? RemoteVersion { get; init; }

    public static CatalogSyncResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };

    public static CatalogSyncResult Ok(int added, int changed, int deleted, CatalogVersionInfo? version = null) =>
        new()
        {
            Success = true,
            Added = added,
            Changed = changed,
            Deleted = deleted,
            RemoteVersion = version,
        };
}

public enum CatalogSyncButtonState
{
    Idle,
    UpdateAvailable,
    Syncing,
    Error,
}
