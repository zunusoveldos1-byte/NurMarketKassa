using NurMarketKassa.Core.Contracts;

namespace NurMarketKassa.Services;

public sealed class WpfLocalStockProvider : ILocalStockProvider
{
    public double GetExpectedQuantity(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId))
            return 0;

        var id = productId.Trim();
        foreach (var product in CatalogCacheService.Products)
        {
            if (string.Equals(product.Id, id, StringComparison.OrdinalIgnoreCase))
                return Math.Max(0, product.Quantity);
        }

        return 0;
    }
}
