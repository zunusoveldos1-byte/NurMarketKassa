using NurMarketKassa.Interfaces;
using NurMarketKassa.Models.Pos;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NurMarketKassa.Services;

public static class ReceiptSnapshotCartEditor
{
    public static void AddProduct(ICartService cart, CatalogProductTileVm product, double qty)
    {
        EnsureCart(cart);
        var root = ParseRoot(cart);
        var items = root["items"] as JsonArray ?? new JsonArray();
        root["items"] = items;

        // Восстанавливаем product_id, если он отсутствует или повреждён
        RepairMissingProductIds(items);

        var unitPrice = ParsePrice(product.PriceLine);
        var productId = product.Id;

        var existing = FindLineByProductId(items, productId);
        if (existing != null)
        {
            // Если товар уже есть в чеке — просто увеличиваем количество, НЕ проверяя MustWeigh
            var oldQty = JsonNumericReader.ToDouble(existing["quantity"]);
            existing["quantity"] = oldQty + qty;

            // Исправляем потенциальный баг, когда is_weight ошибочно стало true после возврата в чек
            if (existing["is_weight"]?.GetValue<bool>() != product.MustWeigh)
            {
                existing["is_weight"] = product.MustWeigh;
                if (existing["product"] is JsonObject productObj)
                {
                    productObj["must_weigh"] = product.MustWeigh;
                }
            }

            RecalcLine(existing);
        }
        else
        {
            items.Add(BuildLine(productId, product.Title, unitPrice, qty, product.MustWeigh));
        }

        if (product.MustWeigh)
            CartDisplayHelper.HintProductWeighedForDisplay(productId);

        RecalcCartTotals(root);
        ApplyRoot(cart, root);
    }

    public static void UpdateLineQuantity(ICartService cart, string itemId, double qty)
    {
        EnsureCart(cart);
        var root = ParseRoot(cart);
        var items = root["items"] as JsonArray ?? new JsonArray();
        RepairMissingProductIds(items);
        root["items"] = items;

        var line = FindLineByItemId(items, itemId);
        if (line == null)
            throw new InvalidOperationException("CartItem not found in this cart.");

        line["quantity"] = IsWeighedLine(line)
            ? JsonNumericReader.RoundWeight(qty)
            : Math.Round(qty, 0);
        RecalcLine(line);
        RecalcCartTotals(root);
        ApplyRoot(cart, root);
    }

    private static bool IsWeighedLine(JsonObject line)
    {
        if (line["is_weight"] is JsonValue w && w.TryGetValue<bool>(out var weighed))
            return weighed;

        using var doc = JsonDocument.Parse(line.ToJsonString());
        return CartDisplayHelper.LineMustWeigh(doc.RootElement);
    }

    public static void RemoveLine(ICartService cart, string itemId)
    {
        EnsureCart(cart);
        var root = ParseRoot(cart);
        if (root["items"] is not JsonArray items)
            return;

        RepairMissingProductIds(items);

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (string.Equals(items[i]?["id"]?.GetValue<string>(), itemId, StringComparison.Ordinal))
                items.RemoveAt(i);
        }

        RecalcCartTotals(root);
        ApplyRoot(cart, root);
    }

    public static void PatchLineDiscount(ICartService cart, string itemId, string? mode, string? value)
    {
        EnsureCart(cart);
        var root = ParseRoot(cart);
        var items = root["items"] as JsonArray ?? new JsonArray();
        RepairMissingProductIds(items);
        root["items"] = items;

        var line = FindLineByItemId(items, itemId);
        if (line == null)
            throw new InvalidOperationException("CartItem not found in this cart.");

        line.Remove("discount_percent");
        line.Remove("discount_total");
        if (mode == null)
        {
            RecalcLine(line);
            RecalcCartTotals(root);
            ApplyRoot(cart, root);
            return;
        }

        if (mode == "percent" && double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
            line["discount_percent"] = pct;
        else if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var sum))
            line["discount_total"] = sum;

        RecalcLine(line);
        RecalcCartTotals(root);
        ApplyRoot(cart, root);
    }

    public static void PatchOrderDiscount(ICartService cart, string? percent, string? total)
    {
        EnsureCart(cart);
        var root = ParseRoot(cart);
        root.Remove("order_discount_percent");
        root.Remove("order_discount_total");

        if (!OrderDiscountHelper.IsEmptyOrZeroLike(percent)
            && double.TryParse(OrderDiscountHelper.NormalizeDecimal(percent!), NumberStyles.Any, CultureInfo.InvariantCulture, out var p)
            && p > 0)
        {
            root["order_discount_percent"] = p;
        }
        else if (!OrderDiscountHelper.IsEmptyOrZeroLike(total)
                 && double.TryParse(OrderDiscountHelper.NormalizeDecimal(total!), NumberStyles.Any, CultureInfo.InvariantCulture, out var t)
                 && t > 0)
        {
            root["order_discount_total"] = t;
        }

        RecalcCartTotals(root);
        ApplyRoot(cart, root);
    }

    public static bool ContainsItemId(ICartService cart, string itemId)
    {
        if (!cart.HasCart || string.IsNullOrEmpty(itemId))
            return false;

        return CartDisplayHelper.EnumerateItems(cart.Root)
            .Any(it => string.Equals(CartDisplayHelper.TryItemId(it), itemId, StringComparison.Ordinal));
    }

    private static void EnsureCart(ICartService cart)
    {
        if (!cart.HasCart)
            throw new InvalidOperationException("Чек не открыт.");
    }

    private static JsonObject ParseRoot(ICartService cart) =>
        CartJsonHelper.ParseCartRoot(cart);

    private static void ApplyRoot(ICartService cart, JsonObject root) =>
        CartJsonHelper.TryApplyObjectToCart(cart, root);

    private static JsonObject? FindLineByItemId(JsonArray? items, string itemId)
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

    private static JsonObject? FindLineByProductId(JsonArray items, string productId)
    {
        if (items == null) return null;
        string normalizedSearchId = productId?.Trim() ?? "";
        if (string.IsNullOrEmpty(normalizedSearchId)) return null;

        foreach (var node in items)
        {
            if (node is not JsonObject obj) continue;

            // Извлекаем ID универсальным методом (работает с числами и строками)
            string? pid = ExtractId(obj["product_id"]);

            // Если нет в корне, пробуем взять из вложенного продукта
            if (string.IsNullOrEmpty(pid) && obj["product"] is JsonObject productObj)
            {
                pid = ExtractId(productObj["id"]);
            }

            // Сравниваем, обрезая лишние пробелы и игнорируя регистр
            if (string.Equals(pid?.Trim(), normalizedSearchId, StringComparison.OrdinalIgnoreCase))
                return obj;
        }
        return null;
    }
    private static JsonObject BuildLine(string productId, string title, double unitPrice, double qty, bool mustWeigh)
    {
        var line = new JsonObject
        {
            ["id"] = "line-" + Guid.NewGuid().ToString("N"),
            ["product_id"] = productId,
            ["product_name"] = title,
            ["quantity"] = qty,
            ["unit_price"] = unitPrice,
            ["is_weight"] = mustWeigh,
            ["product"] = new JsonObject
            {
                ["id"] = productId,
                ["name"] = title,
                ["must_weigh"] = mustWeigh,
            },
        };
        RecalcLine(line);
        return line;
    }

    private static void RecalcLine(JsonObject line)
    {
        var qty = JsonNumericReader.ToDouble(line["quantity"]);
        var unitPrice = JsonNumericReader.ToDouble(line["unit_price"]);
        var gross = qty * unitPrice;
        double discount = 0;
        if (line.TryGetPropertyValue("discount_total", out var dt) && dt != null)
            discount = JsonNumericReader.ToDouble(dt);
        else if (line.TryGetPropertyValue("discount_percent", out var dp) && dp != null)
            discount = gross * JsonNumericReader.ToDouble(dp) / 100.0;

        line["line_total"] = Math.Max(0, gross - discount);
    }

    private static void RecalcCartTotals(JsonObject root)
    {
        using var doc = JsonDocument.Parse(root.ToJsonString());
        var totals = CartTotalsCalculator.Calculate(doc.RootElement);
        root["subtotal"] = totals.Subtotal;
        root["total"] = totals.TotalDue;
        root["total_due"] = totals.TotalDue;
        if (totals.LineCount == 0 && root["totals"] is JsonObject nested)
        {
            nested["total"] = 0;
            nested["grand_total"] = 0;
            nested["amount_due"] = 0;
        }
    }

    private static double ParsePrice(string priceLine)
    {
        if (string.IsNullOrWhiteSpace(priceLine))
            return 0;
        var digits = new string(priceLine.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray())
            .Replace(',', '.');
        return double.TryParse(digits, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;
    }

    public static void RepairMissingProductIds(JsonArray items)
    {
        if (items == null) return;

        foreach (var node in items)
        {
            if (node is not JsonObject line) continue;

            string? pid = ExtractId(line["product_id"]);
            string? innerPid = null;

            if (line["product"] is JsonObject productObj)
            {
                innerPid = ExtractId(productObj["id"]);
            }

            // Сценарий 1: product_id пуст, но есть внутри product["id"]
            if (string.IsNullOrEmpty(pid) && !string.IsNullOrEmpty(innerPid))
            {
                line["product_id"] = innerPid;
            }
            // Сценарий 2: product["id"] пуст, но есть product_id (восстанавливаем вложенный)
            else if (!string.IsNullOrEmpty(pid) && string.IsNullOrEmpty(innerPid) && line["product"] is JsonObject productObj2)
            {
                productObj2["id"] = pid;
            }
            // Сценарий 3: Оба есть, но разные (например, один строка, другой число) - синхронизируем
            else if (!string.IsNullOrEmpty(pid) && !string.IsNullOrEmpty(innerPid)
                     && !string.Equals(pid, innerPid, StringComparison.OrdinalIgnoreCase))
            {
                // Принудительно ставим единый ID (берем из вложенного, как более достоверного)
                line["product_id"] = innerPid;
            }
        }
    }

    private static string? ExtractId(JsonNode? node)
    {
        if (node == null) return null;
        if (node is JsonValue val)
        {
            // Если ID записан как строка
            if (val.TryGetValue<string>(out var s) && !string.IsNullOrEmpty(s))
                return s.Trim();
            // Если ID записан как целое число (например, 12345)
            if (val.TryGetValue<long>(out var l))
                return l.ToString();
            // Если ID записан как число с плавающей точкой
            if (val.TryGetValue<double>(out var d))
                return d.ToString(CultureInfo.InvariantCulture);
        }
        return null;
    }
}