using System.Text.Json;
using NurMarketKassa.Models.Pos;

namespace NurMarketKassa.Services;

/// <summary>Принудительное обновление признаков товара (весовой/штучный) при синхронизации с сайтом.</summary>
public static class CatalogProductClassifier
{
    public static void ApplyWeightFlags(CatalogProductTileVm vm, JsonElement product)
    {
        var mustWeigh = CartDisplayHelper.ProductMustWeigh(product);
        vm.MustWeigh = mustWeigh;

        if (product.TryGetProperty("unit", out var unitEl) && unitEl.ValueKind == JsonValueKind.String)
        {
            var unit = unitEl.GetString()?.Trim();
            if (!string.IsNullOrEmpty(unit))
                vm.Unit = unit;
        }
        else
        {
            vm.Unit = mustWeigh ? "кг" : "шт";
        }

        var qty = StockSyncService.ResolveStockQuantity(product, mustWeigh);
        StockSyncService.ApplyQuantityToTile(vm, qty, mustWeigh);
    }

    public static void ReclassifyTiles(
        List<CatalogProductTileVm> kgList,
        List<CatalogProductTileVm> pieceList)
    {
        var all = kgList.Concat(pieceList).DistinctBy(x => x.Id).ToList();
        kgList.Clear();
        pieceList.Clear();

        foreach (var vm in all)
        {
            if (vm.MustWeigh)
                kgList.Add(vm);
            else
                pieceList.Add(vm);
        }
    }
}
