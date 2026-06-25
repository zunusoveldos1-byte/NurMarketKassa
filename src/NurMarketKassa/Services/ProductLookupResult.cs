using NurMarketKassa.Models.Pos;

namespace NurMarketKassa.Services;

public enum ProductMatchField
{
    None,
    Barcode,
    Name,
    Id,
}

public sealed class ProductLookupItem
{
    public required CatalogProductTileVm Tile { get; init; }

    public ProductMatchField MatchField { get; init; }

    public int Score { get; init; }
}

public sealed class ProductLookupResult
{
    public static ProductLookupResult Empty { get; } = new();

    public IReadOnlyList<ProductLookupItem> Items { get; init; } = Array.Empty<ProductLookupItem>();

    public ProductLookupItem? BestMatch { get; init; }
}
