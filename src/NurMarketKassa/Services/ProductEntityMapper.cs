using System.Globalization;
using NurMarketKassa.Models;
using NurMarketKassa.Models.Pos;

namespace NurMarketKassa.Services;

internal static class ProductEntityMapper
{
    public static CatalogProductTileVm? ToTile(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Id) || string.IsNullOrWhiteSpace(product.Name))
            return null;

        var qty = product.Quantity ?? 0;
        var vm = new CatalogProductTileVm(
            product.Id.Trim(),
            product.Name.Trim(),
            $"{product.Price.ToString("0.00", CultureInfo.InvariantCulture)} сом",
            product.IsWeight,
            product.ImageUrl)
        {
            Barcode = product.Barcode,
            Category = product.Category,
            Brand = product.Brand,
            Unit = product.Unit,
            IsFavorite = product.IsFavorite || CatalogCacheService.FavoriteIds.Contains(product.Id),
        };

        StockSyncService.ApplyQuantityToTile(vm, qty, product.IsWeight);
        return ProductUnitNormalizer.TryPrepareCatalogTile(vm) ? vm : null;
    }
}
