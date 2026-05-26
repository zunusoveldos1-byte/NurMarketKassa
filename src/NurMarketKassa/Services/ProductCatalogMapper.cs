using NurMarketKassa.Models;
using NurMarketKassa.Models.Pos;
using System;
using System.Globalization;
using System.Text.Json;

namespace NurMarketKassa.Services
{
    internal static class ProductCatalogMapper
    {
        public static CatalogProductTileVm? TryTile(JsonElement p, string apiBaseUrl)
        {
            string? id = TryId(p);
            if (string.IsNullOrEmpty(id))
                return null;

            string title = Title(p);
            double? price = TryPrice(p);
            string priceLine = price is null ? "—" : $"{price.Value.ToString("0.00", CultureInfo.InvariantCulture)} сом";
            bool mustWeigh = CartDisplayHelper.ProductMustWeigh(p);
            string? imageUrl = ProductImageUrl.TryGet(p, apiBaseUrl);

            // ---------- Barcode ----------
            string? barcode = null;
            foreach (string key in new[] { "barcode", "sku", "article", "code", "ean" })
            {
                if (p.TryGetProperty(key, out var bVal))
                {
                    string? s = bVal.ValueKind switch
                    {
                        JsonValueKind.String => bVal.GetString(),
                        JsonValueKind.Number => bVal.GetRawText(),
                        _ => null
                    };
                    if (!string.IsNullOrWhiteSpace(s) && s != "0")
                    {
                        barcode = s;
                        break;
                    }
                }
            }

            CatalogProductTileVm vm = new CatalogProductTileVm(id, title, priceLine, mustWeigh, imageUrl);
            vm.Barcode = barcode;

            // ---------- Quantity ----------
            double qty = 0;
            if (p.TryGetProperty("quantity", out var qEl) && TryGetDouble(qEl, out double qVal))
                qty = qVal;
            else if (p.TryGetProperty("stock_quantity", out var sqEl) && TryGetDouble(sqEl, out double sqVal))
                qty = sqVal;
            vm.Quantity = qty;

            // ---------- StockInfo ----------
            string? stockInfo = null;
            if (p.TryGetProperty("stock_quantity", out var stEl) && TryGetDouble(stEl, out double stVal))
            {
                stockInfo = !mustWeigh
                    ? $"{stVal:F0} шт."
                    : $"{stVal:F2} кг";
            }
            else if (p.TryGetProperty("stock_weight", out var swEl) && TryGetDouble(swEl, out double swVal))
            {
                stockInfo = $"{swVal:F2} кг";
            }
            vm.StockInfo = stockInfo;

            // ---------- PurchasePrice ----------
            if (p.TryGetProperty("purchase_price", out var ppEl) && TryGetDouble(ppEl, out double ppVal))
                vm.PurchasePrice = ppVal;

            // ---------- Unit ----------
            if (p.TryGetProperty("unit", out var unitEl) && unitEl.ValueKind == JsonValueKind.String)
            {
                string? unit = unitEl.GetString()?.Trim();
                vm.Unit = string.IsNullOrEmpty(unit) ? (mustWeigh ? "кг" : "шт") : unit;
            }
            else
            {
                vm.Unit = mustWeigh ? "кг" : "шт";
            }

            // Категория и бренд
            if (p.TryGetProperty("category", out var cat) && cat.ValueKind == JsonValueKind.String)
                vm.Category = cat.GetString();
            if (p.TryGetProperty("brand", out var brd) && brd.ValueKind == JsonValueKind.String)
                vm.Brand = brd.GetString();

            // Статус и горячая клавиша (если нужны для фильтра)
            if (p.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String)
                vm.Status = st.GetString();
            if (p.TryGetProperty("hotkey_group", out var hk) && hk.ValueKind == JsonValueKind.String)
                vm.HotkeyGroup = hk.GetString();

            // Восстановление избранного из локального кэша
            vm.IsFavorite = CatalogCacheService.FavoriteIds.Contains(vm.Id);

            return vm;
        }

        public static CatalogProductTileVm? TryTile(ProductDto dto, string apiBaseUrl)
        {
            if (dto == null) return null;
            if (string.IsNullOrEmpty(dto.Id) || string.IsNullOrEmpty(dto.Title))
                return null;

            return new CatalogProductTileVm(dto.Id, dto.Title, dto.Price.ToString(CultureInfo.InvariantCulture), dto.MustWeigh, dto.ImageUrl)
            {
                Barcode = dto.Barcode,
                Category = dto.Category,
                Brand = dto.Brand,
                Quantity = dto.Quantity,
                // ↓↓↓ исправлено ↓↓↓
                Unit = !string.IsNullOrWhiteSpace(dto.Unit)
                    ? dto.Unit.Trim()
                    : (dto.MustWeigh ? "кг" : "шт")
            };
        }

        public static string Title(JsonElement p)
        {
            if (p.ValueKind != JsonValueKind.Object)
                return "—";

            foreach (string key in new[] { "name", "title", "display_name", "label" })
            {
                if (p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    string? s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s.Trim();
                }
            }

            string? id = TryId(p);
            return string.IsNullOrEmpty(id) ? "—" : $"Товар #{id}";
        }

        public static string? TryId(JsonElement p)
        {
            if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("id", out var idProp))
                return null;

            return idProp.ValueKind switch
            {
                JsonValueKind.String => string.IsNullOrWhiteSpace(idProp.GetString()) ? null : idProp.GetString(),
                JsonValueKind.Number => idProp.GetRawText(),
                _ => null
            };
        }

        public static double? TryPrice(JsonElement p)
        {
            if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("price", out var v))
                return null;

            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d))
                return d;
            if (v.ValueKind == JsonValueKind.String &&
                double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double x))
                return x;

            return null;
        }

        private static bool TryGetDouble(JsonElement element, out double value)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.TryGetDouble(out value);
            if (element.ValueKind == JsonValueKind.String)
            {
                string? s = element.GetString();
                if (s != null && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    return true;
            }
            value = 0;
            return false;
        }
    }
}