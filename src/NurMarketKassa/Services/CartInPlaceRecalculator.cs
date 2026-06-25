using System.Text.Json;
using System.Text.Json.Nodes;
using NurMarketKassa.Interfaces;

namespace NurMarketKassa.Services;

/// <summary>Пересчёт line_total и итогов корзины в JSON без запроса к API.</summary>
internal static class CartInPlaceRecalculator
{
    public static void EnsureRecalculated(ICartService cart)
    {
        if (!cart.HasCart)
            return;

        var root = CartJsonHelper.ParseCartRoot(cart);
        RecalculateRoot(root);
        CartJsonHelper.TryApplyObjectToCart(cart, root);
    }

    public static void UpdateLineQuantity(ICartService cart, string itemId, double qty, bool weighed)
    {
        if (!cart.HasCart)
            return;

        var root = CartJsonHelper.ParseCartRoot(cart);
        var line = FindLine(root["items"] as JsonArray, itemId);
        if (line == null)
            return;

        line["quantity"] = weighed ? JsonNumericReader.RoundWeight(qty) : Math.Round(qty, 0);
        RecalculateLine(line);
        RecalculateRoot(root);
        CartJsonHelper.TryApplyObjectToCart(cart, root);
    }

    private static JsonObject? FindLine(JsonArray? items, string itemId)
    {
        if (items == null)
            return null;

        foreach (var node in items)
        {
            if (node is not JsonObject obj)
                continue;
            if (string.Equals(obj["id"]?.GetValue<string>(), itemId, StringComparison.Ordinal))
                return obj;
        }

        return null;
    }

    private static void RecalculateLine(JsonObject line)
    {
        var qty = JsonNumericReader.ToDouble(line["quantity"]);
        var unitPrice = JsonNumericReader.ToDouble(line["unit_price"]);
        var gross = qty * unitPrice;

        double discount = 0;
        if (line.TryGetPropertyValue("discount_total", out var discountTotal) && discountTotal != null)
            discount = JsonNumericReader.ToDouble(discountTotal);
        else if (line.TryGetPropertyValue("discount_percent", out var discountPercent) && discountPercent != null)
            discount = gross * JsonNumericReader.ToDouble(discountPercent) / 100.0;

        line["line_total"] = Math.Max(0, gross - discount);
    }

    private static void RecalculateRoot(JsonObject root)
    {
        if (root["items"] is JsonArray items)
        {
            foreach (var node in items)
            {
                if (node is JsonObject line)
                    RecalculateLine(line);
            }
        }

        using var doc = JsonDocument.Parse(root.ToJsonString());
        var totals = CartTotalsCalculator.Calculate(doc.RootElement);
        root["subtotal"] = totals.Subtotal;
        root["total"] = totals.TotalDue;
        root["total_due"] = totals.TotalDue;

        if (root["totals"] is JsonObject nested)
        {
            nested["total"] = totals.TotalDue;
            nested["grand_total"] = totals.TotalDue;
            nested["amount_due"] = totals.TotalDue;
            nested["subtotal"] = totals.Subtotal;
        }
    }
}
