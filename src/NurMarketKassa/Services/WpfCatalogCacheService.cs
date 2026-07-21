using System.Linq;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Models.Pos;

namespace NurMarketKassa.Services;

public sealed class WpfCatalogCacheService : ICatalogCacheService
{
    public Task<CatalogSyncResult> SyncCatalogFullAsync(CancellationToken cancellationToken = default) =>
        CatalogCacheService.SyncCatalogFullAsync(cancellationToken);

    public bool TryLoadFromDatabase() => CatalogCacheService.LoadFromDatabase();

    public IReadOnlyList<CatalogProductTileVm> GetProducts() =>
        CatalogCacheService.Products.ToList();
}
