using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NurMarketKassa.Models.Pos;

namespace NurMarketKassa.Services;

/// <summary>Синхронизация остатков каталога с API и корректировка после продаж.</summary>
public static class StockSyncService
{
    public static double ResolveStockQuantity(JsonElement product, bool mustWeigh)
    {
        if (product.ValueKind != JsonValueKind.Object)
            return 0;

        if (mustWeigh)
        {
            if (TryReadDouble(product, "stock_weight") is { } sw)
                return sw;
            if (TryReadDouble(product, "stock_quantity") is { } sq)
                return sq;
        }
        else
        {
            if (TryReadDouble(product, "stock_quantity") is { } sq)
                return sq;
            if (TryReadDouble(product, "quantity") is { } q)
                return q;
        }

        return TryReadDouble(product, "quantity") ?? 0;
    }

    public static void ApplyQuantityToTile(CatalogProductTileVm vm, double quantity, bool mustWeigh)
    {
        vm.Quantity = quantity;
        vm.StockInfo = mustWeigh
            ? $"{quantity:F2} кг"
            : $"{quantity:F0} шт.";
    }

    public static async Task OverlayAgentStockAsync(
        IEnumerable<CatalogProductTileVm> products,
        CancellationToken ct = default)
    {
        List<JsonElement> agentProducts;
        try
        {
            agentProducts = await App.Api.GetAgentProductsAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (agentProducts.Count == 0)
            return;

        var qtyMap = BuildQuantityMap(agentProducts);
        foreach (var vm in products)
        {
            if (!qtyMap.TryGetValue(vm.Id, out var qty))
                continue;
            ApplyQuantityToTile(vm, qty, vm.MustWeigh);
        }
    }

    public static void DecrementLocalStock(string productId, double soldQty)
    {
        if (string.IsNullOrWhiteSpace(productId) || soldQty <= 0)
            return;

        var id = productId.Trim();
        foreach (var vm in CatalogCacheService.Products)
        {
            if (!string.Equals(vm.Id, id, StringComparison.OrdinalIgnoreCase))
                continue;

            var next = Math.Max(0, vm.Quantity - soldQty);
            ApplyQuantityToTile(vm, next, vm.MustWeigh);
            break;
        }
    }

    public static async Task RefreshSoldItemsStockAsync(JsonElement cart, CancellationToken ct = default)
    {
        foreach (var line in CartDisplayHelper.EnumerateItems(cart))
        {
            var productId = CartDisplayHelper.TryProductId(line);
            if (string.IsNullOrEmpty(productId))
                continue;

            var soldQty = CartDisplayHelper.LineQuantity(line);
            DecrementLocalStock(productId, soldQty);

            try
            {
                var detail = await App.Api.ProductsDetailAsync(productId, ct).ConfigureAwait(false);
                if (detail is not { } el)
                    continue;

                var mustWeigh = CartDisplayHelper.ProductMustWeigh(el);
                var qty = ResolveStockQuantity(el, mustWeigh);
                foreach (var vm in CatalogCacheService.Products)
                {
                    if (!string.Equals(vm.Id, productId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    CatalogProductClassifier.ApplyWeightFlags(vm, el);
                    ApplyQuantityToTile(vm, qty, vm.MustWeigh);
                    break;
                }
            }
            catch
            {
                /* локальная корректировка уже применена */
            }
        }

        CatalogCacheService.SaveToFile();
    }

    private static Dictionary<string, double> BuildQuantityMap(IEnumerable<JsonElement> items)
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in items)
        {
            if (!el.TryGetProperty("id", out var idEl))
                continue;

            var id = idEl.ValueKind switch
            {
                JsonValueKind.Number => idEl.GetRawText(),
                JsonValueKind.String => idEl.GetString() ?? "",
                _ => "",
            };
            if (string.IsNullOrEmpty(id))
                continue;

            var mustWeigh = CartDisplayHelper.ProductMustWeigh(el);
            map[id] = ResolveStockQuantity(el, mustWeigh);
        }

        return map;
    }

    private static double? TryReadDouble(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var v))
            return null;

        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetDouble(out var d) ? d : null,
            JsonValueKind.String => double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : null,
            _ => null,
        };
    }
}
