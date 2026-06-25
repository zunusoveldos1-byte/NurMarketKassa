using System.Globalization;
using System.Text.Json;
using NurMarketKassa.Interfaces;

namespace NurMarketKassa.Services;

public sealed class StockLineStatus
{
    public double Warehouse { get; init; }

    public double Reserved { get; init; }

    public double Available { get; init; }

    public double LineQty { get; init; }

    public bool IsInsufficient { get; init; }
}

/// <summary>
/// Доступный остаток с учётом резерва в отложенных чеках.
/// </summary>
public static class StockAvailabilityService
{
    public static double GetWarehouseQuantity(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId))
            return 0;

        var tile = CatalogCacheService.Products.FirstOrDefault(p =>
            string.Equals(p.Id, productId, StringComparison.OrdinalIgnoreCase));
        return tile?.Quantity ?? 0;
    }

    /// <summary>Сумма товара во всех отложенных чеках, кроме текущего открытого.</summary>
    public static double CalculateReservedQuantity(string productId, string? excludeDeferredEntryId = null)
    {
        if (string.IsNullOrWhiteSpace(productId))
            return 0;

        double sum = 0;
        foreach (var entry in DeferredCartsStore.LoadAll())
        {
            if (!string.IsNullOrEmpty(excludeDeferredEntryId) &&
                string.Equals(entry.Id, excludeDeferredEntryId, StringComparison.Ordinal))
                continue;

            sum += SumProductQuantityInCartJson(entry.CartJson, productId);
        }

        return sum;
    }

    public static double GetCurrentCartQuantity(string productId, ICartService cart)
    {
        if (string.IsNullOrWhiteSpace(productId) || !cart.HasCart)
            return 0;

        return CartDisplayHelper.EnumerateItems(cart.Root)
            .Where(it => string.Equals(CartDisplayHelper.TryProductId(it), productId, StringComparison.OrdinalIgnoreCase))
            .Sum(CartDisplayHelper.LineQuantity);
    }

    /// <summary>Сколько ещё можно добавить в текущий чек.</summary>
    public static double GetAvailableToAdd(string productId, ICartService cart, string? excludeDeferredEntryId = null)
    {
        var warehouse = GetWarehouseQuantity(productId);
        var reserved = CalculateReservedQuantity(productId, excludeDeferredEntryId);
        var inCart = GetCurrentCartQuantity(productId, cart);
        var available = warehouse - reserved - inCart;
        LogAvailability(productId, warehouse, reserved, available);
        return Math.Max(0, available);
    }

    /// <summary>Доступно для продажи по строке (без вычета текущей корзины).</summary>
    public static double GetAvailableForLine(string productId, string? excludeDeferredEntryId = null)
    {
        var warehouse = GetWarehouseQuantity(productId);
        var reserved = CalculateReservedQuantity(productId, excludeDeferredEntryId);
        var available = Math.Max(0, warehouse - reserved);
        LogAvailability(productId, warehouse, reserved, available);
        return available;
    }

    public static bool CanAddQuantity(string productId, double qtyToAdd, ICartService cart, string? excludeDeferredEntryId = null)
    {
        if (qtyToAdd <= 0)
            return true;

        var available = GetAvailableToAdd(productId, cart, excludeDeferredEntryId);
        return available + 1e-6 >= qtyToAdd;
    }

    public static StockLineStatus EvaluateCartLine(string productId, double lineQty, string? excludeDeferredEntryId = null)
    {
        var warehouse = GetWarehouseQuantity(productId);
        var reserved = CalculateReservedQuantity(productId, excludeDeferredEntryId);
        var available = Math.Max(0, warehouse - reserved);
        var insufficient = lineQty > available + 1e-6;

        if (insufficient)
        {
            PosLogger.Log(
                $"Payment blocked: insufficient stock Product={productId} Required={lineQty.ToString(CultureInfo.InvariantCulture)} Available={available.ToString(CultureInfo.InvariantCulture)}",
                "STOCK");
        }

        return new StockLineStatus
        {
            Warehouse = warehouse,
            Reserved = reserved,
            Available = available,
            LineQty = lineQty,
            IsInsufficient = insufficient,
        };
    }

    public static IReadOnlyList<(string Title, StockLineStatus Status)> EvaluateCurrentCart(ICartService cart, string? excludeDeferredEntryId = null)
    {
        if (!cart.HasCart)
            return Array.Empty<(string, StockLineStatus)>();

        var issues = new List<(string, StockLineStatus)>();
        foreach (var it in CartDisplayHelper.EnumerateItems(cart.Root))
        {
            var productId = CartDisplayHelper.TryProductId(it);
            if (string.IsNullOrEmpty(productId))
                continue;

            var qty = CartDisplayHelper.LineQuantity(it);
            var status = EvaluateCartLine(productId, qty, excludeDeferredEntryId);
            if (status.IsInsufficient)
                issues.Add((CartDisplayHelper.ItemName(it), status));
        }

        return issues;
    }

    private static double SumProductQuantityInCartJson(string cartJson, string productId)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(cartJson) ? "{}" : cartJson);
            return CartDisplayHelper.EnumerateItems(doc.RootElement)
                .Where(it => string.Equals(CartDisplayHelper.TryProductId(it), productId, StringComparison.OrdinalIgnoreCase))
                .Sum(CartDisplayHelper.LineQuantity);
        }
        catch
        {
            return 0;
        }
    }

    private static void LogAvailability(string productId, double warehouse, double reserved, double available)
    {
        PosLogger.Log(
            $"Product={productId} Warehouse={warehouse.ToString(CultureInfo.InvariantCulture)} Reserved={reserved.ToString(CultureInfo.InvariantCulture)} Available={available.ToString(CultureInfo.InvariantCulture)}",
            "STOCK");
    }
}
