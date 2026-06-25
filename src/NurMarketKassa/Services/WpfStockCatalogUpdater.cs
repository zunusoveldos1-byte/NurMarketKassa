using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Models.Pos;

namespace NurMarketKassa.Services;

public sealed class WpfStockCatalogUpdater : IStockCatalogUpdater
{
    public void UpdateCatalogStock(string productId, double quantity)
    {
        if (string.IsNullOrWhiteSpace(productId))
            return;

        var id = productId.Trim();
        foreach (var vm in CatalogCacheService.Products)
        {
            if (!string.Equals(vm.Id, id, StringComparison.OrdinalIgnoreCase))
                continue;

            StockSyncService.ApplyQuantityToTile(vm, Math.Max(0, quantity), vm.MustWeigh);
            break;
        }
    }
}
