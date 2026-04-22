using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace NurMarketKassa.Services;

internal static class CartReceiptTextBuilder
{
    private static int W => ReceiptLayout.CharWidth;

    /// <summary>
    /// offlineNote — если передан, добавляется предупреждение (например «ОФФЛАЙН»).
    /// </summary>
    internal static string BuildSimpleReceipt(
        string cartJson,
        string? offlineNote = null,
        string? paymentMethodKey = null,
        string? cashReceived = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(cartJson) ? "{}" : cartJson);
            var root = doc.RootElement;
            var sb = new StringBuilder(900);

            void Line(string s = "") { sb.Append(s); sb.Append('\n'); }
            void Dash() => Line(new string('-', W));

            // ─── Header (как на эталонном чеке) ───────────────────────────────
            var welcome = Env("DESKTOP_MARKET_RECEIPT_WELCOME", "Добро пожаловать");
            var store = Env("DESKTOP_MARKET_RECEIPT_STORE_NAME", "MARKET PLUS");
            var address = Env("DESKTOP_MARKET_RECEIPT_ADDRESS", "");

            Line(Center(welcome, W));
            Line(Center(store, W));
            foreach (var a in SplitLines(address))
                Line(Center(a, W));
            Line();

            var receiptNo = TryReceiptNumber(root);
            if (!string.IsNullOrEmpty(receiptNo))
                Line(Center($"Чек №: {PrettyReceiptNumber(receiptNo)}", W));

            var now = DateTime.Now;
            Line(Center(
                $"Дата: {now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)}",
                W));

            if (!string.IsNullOrWhiteSpace(offlineNote))
            {
                Line();
                Dash();
                Line(Center("!! " + offlineNote.Trim() + " !!", W));
            }

            Dash();

            // ─── Позиции: одна строка «1. Товар … N x цена = сумма» ───────────
            var itemIndex = 0;
            foreach (var it in CartDisplayHelper.EnumerateItems(root))
            {
                itemIndex++;
                var name = CartDisplayHelper.ItemName(it).Trim();
                var qty = CartDisplayHelper.LineQuantity(it);
                var unitPrice = CartDisplayHelper.UnitPrice(it);
                var lineTotal = LineTotalDouble(it);
                Line(FormatItemLine(itemIndex, name, qty, unitPrice, lineTotal, W));

                var disc = TryLineDiscount(it);
                if (disc > 1e-6)
                    Line(PadBoth("СКИДКА:", "-" + FormatMoney(disc), W));
            }

            if (itemIndex == 0)
                Line(Center("(НЕТ ПОЗИЦИЙ)", W));

            // ─── Итоги ─────────────────────────────────────────────────────────
            var subtotal = TryCartSubtotal(root);
            var orderDisc = TryOrderDiscount(root);
            var total = CartDisplayHelper.TotalDue(root);
            Dash();
            var subToShow = subtotal >= 0 ? subtotal : total + orderDisc;
            Line(PadBoth("ИТОГО:", FormatMoney(subToShow), W));
            if (orderDisc > 1e-6)
                Line(PadBoth("СКИДКА:", FormatMoney(orderDisc), W));
            Line(PadBoth("К ОПЛАТЕ:", FormatMoney(total), W));

            var pm = (paymentMethodKey ?? "").Trim().ToLowerInvariant();
            var cash = ParseMoneyOrNull(cashReceived);
            if (pm.Length > 0 && cash.HasValue)
            {
                var pmLabel = pm switch
                {
                    "cash" => "НАЛИЧНЫМИ",
                    "transfer" => "БЕЗНАЛ",
                    "card" => "БЕЗНАЛ",
                    _ => pm.ToUpperInvariant(),
                };
                Line();
                Line(PadBoth("ОПЛАТА: " + pmLabel, FormatMoney(cash.Value), W));
                if (pm is "cash")
                {
                    var change = Math.Max(0, cash.Value - total);
                    Line(PadBoth("СДАЧА:", FormatMoney(change), W));
                }
            }

            Dash();
            Line(Center("СПАСИБО ЗА ПОКУПКУ!", W));
            Line();

            return sb.ToString().ToUpperInvariant();
        }
        catch
        {
            return "NUR MARKET\n\n(НЕ УДАЛОСЬ РАЗОБРАТЬ КОРЗИНУ)\n\n".ToUpperInvariant();
        }
    }

    /// <summary>Номер чека для печати: чисто цифры — с ведущими нулями (6), иначе как в данных.</summary>
    private static string PrettyReceiptNumber(string raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0)
            return t;
        if (t.All(char.IsAsciiDigit) && ulong.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var u))
            return u.ToString("D6", CultureInfo.InvariantCulture);
        return t;
    }

    /// <summary>Одна строка товара: «N. Название… Q x U = T» в ширину W.</summary>
    private static string FormatItemLine(int index, string name, double qty, double unitPrice, double lineTotal, int width)
    {
        var qtyStr = FormatQty(qty);
        var unitStr = FormatMoney(unitPrice);
        var totalStr = FormatMoney(lineTotal);
        var rightCore = $"{qtyStr} x {unitStr} = {totalStr}";
        var prefix = $"{index}. ";
        const int minNameChars = 2;
        var maxRight = width - prefix.Length - minNameChars;
        if (maxRight < 10)
            maxRight = Math.Max(10, width - prefix.Length - 1);
        var idealRight = Math.Min(20, maxRight);
        var rightCol = Math.Min(Math.Max(idealRight, rightCore.Length), maxRight);
        if (rightCol < rightCore.Length)
        {
            var fb = prefix + rightCore;
            return fb.Length <= width ? fb : fb[..width];
        }

        var right = rightCore.PadLeft(rightCol);
        var nameBudget = width - prefix.Length - rightCol;
        if (nameBudget < 1)
        {
            var fallback = prefix + name.Trim() + " " + rightCore;
            return fallback.Length <= width ? fallback : fallback[..width];
        }

        var raw = name.Trim();
        if (raw.Length > nameBudget)
            raw = raw[..nameBudget];
        else
            raw = raw.PadRight(nameBudget);
        return prefix + raw + right;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static double LineTotalDouble(JsonElement it)
    {
        foreach (var key in new[]
                 {
                     "line_total", "line_total_amount", "line_amount", "amount",
                     "total", "sum", "total_price", "subtotal", "line_sum",
                 })
        {
            if (it.TryGetProperty(key, out var v))
            {
                var d = v.ValueKind switch
                {
                    JsonValueKind.Number => v.TryGetDouble(out var x) ? x : (double?)null,
                    JsonValueKind.String => double.TryParse(
                        v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : null,
                    _ => null,
                };
                if (d.HasValue) return d.Value;
            }
        }

        var qty = CartDisplayHelper.LineQuantity(it);
        var up  = CartDisplayHelper.UnitPrice(it);
        return qty * up;
    }

    private static string FormatMoney(double v) =>
        v.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatQty(double qty)
    {
        if (qty == Math.Floor(qty) && qty >= 0 && qty < 1_000_000)
            return ((long)qty).ToString(CultureInfo.InvariantCulture);
        return qty.ToString("0.000", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
    }

    /// <summary>Centre-align text within <paramref name="width"/>.</summary>
    private static string Center(string s, int width)
    {
        if (s.Length >= width) return s;
        var total = width - s.Length;
        var left  = total / 2;
        return s.PadLeft(s.Length + left).PadRight(width);
    }

    /// <summary>Put <paramref name="left"/> flush-left and <paramref name="right"/> flush-right.</summary>
    private static string PadBoth(string left, string right, int width)
    {
        var gap = width - left.Length - right.Length;
        if (gap <= 0) return left + " " + right;
        return left + new string(' ', gap) + right;
    }

    /// <summary>Номер на термочеке: короткий код из UUID (8 hex) или усечённый человекочитаемый номер.</summary>
    private static string FormatReceiptRef(string raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0)
            return t;

        var hexOnly = new string(t.Where(char.IsAsciiHexDigit).ToArray());
        if (hexOnly.Length >= 32)
            return hexOnly[..8].ToUpperInvariant();
        if (hexOnly.Length >= 16)
            return hexOnly[..12].ToUpperInvariant();
        if (hexOnly.Length >= 8)
            return hexOnly[..8].ToUpperInvariant();

        if (t.Length <= 12)
            return t;
        return t[^Math.Min(10, t.Length)..];
    }

    private static double TryLineDiscount(JsonElement it)
    {
        foreach (var key in new[] { "discount_total", "line_discount", "discount" })
        {
            if (it.ValueKind == JsonValueKind.Object && it.TryGetProperty(key, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
                    return d;
                if (v.ValueKind == JsonValueKind.String &&
                    double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x))
                    return x;
            }
        }
        return 0;
    }

    private static double TryOrderDiscount(JsonElement cart)
    {
        foreach (var key in new[] { "order_discount_total", "discount_total", "order_discount" })
        {
            if (cart.ValueKind == JsonValueKind.Object && cart.TryGetProperty(key, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
                    return d;
                if (v.ValueKind == JsonValueKind.String &&
                    double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x))
                    return x;
            }
        }
        return 0;
    }

    private static double TryCartSubtotal(JsonElement cart)
    {
        foreach (var key in new[] { "subtotal", "sub_total", "subtotal_amount" })
        {
            if (cart.ValueKind == JsonValueKind.Object && cart.TryGetProperty(key, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
                    return d;
                if (v.ValueKind == JsonValueKind.String &&
                    double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x))
                    return x;
            }
        }
        return -1;
    }

    private static string Env(string key, string fallback)
    {
        try
        {
            var v = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }
        catch
        {
            return fallback;
        }
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        var t = (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (t.Length == 0)
            yield break;
        foreach (var line in t.Split('\n'))
        {
            var s = line.Trim();
            if (s.Length > 0)
                yield return s;
        }
    }

    private static string? TryReceiptNumber(JsonElement cart)
    {
        if (cart.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var key in new[]
                 {
                     "receipt_number", "receipt_no", "check_number", "check_no", "sale_number", "number", "seq",
                     "sale_id", "order_id", "pos_sale_id",
                 })
        {
            if (cart.TryGetProperty(key, out var v))
            {
                var s = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.GetRawText(),
                    _ => null,
                };
                s = (s ?? "").Trim();
                if (s.Length > 0)
                    return FormatReceiptRef(s);
            }
        }

        if (cart.TryGetProperty("sale", out var sale) && sale.ValueKind == JsonValueKind.Object &&
            sale.TryGetProperty("id", out var sid))
        {
            var s = sid.ValueKind switch
            {
                JsonValueKind.String => sid.GetString(),
                JsonValueKind.Number => sid.GetRawText(),
                _ => null,
            };
            s = (s ?? "").Trim();
            if (s.Length > 0)
                return FormatReceiptRef(s);
        }

        var id = CartDisplayHelper.TryCartId(cart);
        return string.IsNullOrEmpty(id) ? null : FormatReceiptRef(id);
    }

    private static double? ParseMoneyOrNull(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length == 0)
            return null;
        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : null;
    }
}
