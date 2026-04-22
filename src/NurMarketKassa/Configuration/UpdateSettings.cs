namespace NurMarketKassa.Configuration;

/// <summary>
/// Проверка обновлений: JSON-манифест с полями latest_version и download_url (HTTPS).
/// Разместите файл на своём сервере и укажите полный URL в ManifestUrl.
/// </summary>
public sealed class UpdateSettings
{
    /// <summary>Полный HTTPS URL JSON (см. update-manifest.example.json в репозитории).</summary>
    public string? ManifestUrl { get; init; }

    public bool CheckOnStartup { get; init; } = true;

    /// <summary>Не чаще одной проверки за указанное число часов (0 = без ограничения).</summary>
    public int MinHoursBetweenChecks { get; init; } = 4;
}
