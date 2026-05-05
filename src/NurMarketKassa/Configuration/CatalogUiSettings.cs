
#nullable disable
namespace NurMarketKassa.Configuration;

public sealed class CatalogUiSettings
{
    public int QuickCatalogLimit { get; init; } = 120;

    public int CatalogMaxPages { get; init; } = 6;

    public int SearchLimit { get; init; } = 40;

    public int SearchDebounceMs { get; init; } = 380;
}
