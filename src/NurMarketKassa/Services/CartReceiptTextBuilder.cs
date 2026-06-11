using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace NurMarketKassa.Services;

internal static class CartReceiptTextBuilder
{
    private static int W => ReceiptLayout.CharWidth;

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
            void Blank() => Line();

            // ─── Header ───────────────────────────────────────────────
            var store = UserPreferences.Instance.StoreName ?? "MARKET PLUS";

            Line(Center(store, W));
            Blank();

            var receiptNo = TryReceiptNumber(root);
            if (!string.IsNullOrEmpty(receiptNo))
                Line($"Чек №: {PrettyReceiptNumber(receiptNo)}");

            var now = DateTime.Now;
            Line($"Дата: {now:dd.MM.yyyy HH:mm}");

            if (!string.IsNullOrWhiteSpace(offlineNote))
            {
                Blank();
                Dash();
                Line(Center("!! " + offlineNote.Trim() + " !!", W));
                Dash();
            }
            else
            {
                Dash();
            }
            Blank();

            // ─── Items ────────────────────────────────────────────────
            int itemIndex = 0;
            foreach (var it in CartDisplayHelper.EnumerateItems(root))
            {
                itemIndex++;
                var name = CartDisplayHelper.ItemName(it).Trim();
                var qty = CartDisplayHelper.LineQuantity(it);
                var unitPrice = CartDisplayHelper.UnitPrice(it);
                var lineTotal = LineTotalDouble(it);
                var isWeight = CartDisplayHelper.LineMustWeigh(it);

                var qtyStr = isWeight ? FormatQty(qty) : qty.ToString("0");
                var unitStr = FormatMoney(unitPrice);
                var totalStr = FormatMoney(lineTotal);

                foreach (var line in WrapText(name, W))
                    Line(line);

                Line(PadBoth($"{qtyStr} x {unitStr}", totalStr, W));
                Blank();

                var disc = TryLineDiscount(it);
                if (disc > 1e-6)
                    Line(PadBoth("СКИДКА:", "-" + FormatMoney(disc), W));
            }

            if (itemIndex == 0)
                Line(Center("(НЕТ ПОЗИЦИЙ)", W));

            Blank();
            Dash();
            Blank();

            // ─── Totals ────────────────────────────────────────────────
            var totals = CartTotalsCalculator.Calculate(root);
            if (totals.LineDiscounts > 1e-6)
                Line(PadBoth("СКИДКА ПОЗИЦИЙ:", "-" + FormatMoney(totals.LineDiscounts), W));
            if (totals.OrderDiscount > 1e-6)
                Line(PadBoth("СКИДКА НА ЧЕК:", "-" + FormatMoney(totals.OrderDiscount), W));
            Line(PadBoth("ПРОМЕЖУТОЧНЫЙ ИТОГ:", FormatMoney(totals.Subtotal), W));
            Line(PadBoth("ИТОГО:", FormatMoney(totals.TotalDue), W));
            Blank();

            // ─── Payment ───────────────────────────────────────────────
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
                Line(PadBoth("ОПЛАТА: " + pmLabel, FormatMoney(cash.Value), W));
                if (pm is "cash")
                {
                    var change = CartTotalsCalculator.CalculateChange(cash.Value, totals.TotalDue);
                    Line(PadBoth("СДАЧА:", FormatMoney(change), W));
                }
            }

            Blank();
            Dash();
            Blank();

            // ─── Footer ────────────────────────────────────────────────
            // Изменили текст на более компактную фразу и добавили пустые строки
            Line(Center("Спасибо за покупку!", W));
            Blank();
            Line(Center("Приходите ещё!", W));

            // ===== ДОБАВЛЕНО: пустые строки для подачи бумаги =====
            Blank();
            Blank();
            Blank();
            // =======================================================

            return sb.ToString().ToUpperInvariant();
        }
        catch
        {
            return "NUR MARKET\n\n(НЕ УДАЛОСЬ РАЗОБРАТЬ КОРЗИНУ)\n\n".ToUpperInvariant();
        }
    }

    private static string PrettyReceiptNumber(string raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0)
            return t;
        if (t.All(char.IsAsciiDigit) && ulong.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var u))
            return u.ToString("D6", CultureInfo.InvariantCulture);
        return t;
    }

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
        var up = CartDisplayHelper.UnitPrice(it);
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

    private static IEnumerable<string> WrapText(string text, int width)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        while (text.Length > width)
        {
            int wrap = text.LastIndexOf(' ', width);

            if (wrap <= 0)
                wrap = width;

            yield return text[..wrap].TrimEnd();
            text = text[wrap..].TrimStart();
        }

        if (text.Length > 0)
            yield return text;
    }

    private static string Center(string s, int width)
    {
        if (s.Length >= width) return s;
        var total = width - s.Length;
        var left = total / 2;
        return s.PadLeft(s.Length + left).PadRight(width);
    }

    private static string PadBoth(string left, string right, int width)
    {
        var gap = width - left.Length - right.Length;
        if (gap < 1)
            return left + Environment.NewLine + right.PadLeft(width);
        return left + new string(' ', gap) + right;
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
                    return s;
            }
        }

        return null;
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

    private static double? ParseMoneyOrNull(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length == 0)
            return null;
        return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : null;
    }
}