using System.Text.Json;
using NurMarketKassa.Interfaces;

namespace NurMarketKassa.Services;

/// <summary>Извлечение корзины из ответа scan/patch (как _cart_from_patch_response + ответ scan).</summary>
internal static class CartResponseHelper
{
    public static bool TryUpdateCartSession(JsonElement response, ICartService session)
    {
        var cart = ExtractCart(response);
        if (cart == null)
            return false;

        if (cart.Value.ValueKind != JsonValueKind.Object)
        {
            session.Clear();
            return false;
        }

        try
        {
            session.SetCart(cart.Value);
            CartInPlaceRecalculator.EnsureRecalculated(session);
            return true;
        }
        catch (InvalidOperationException)
        {
            session.Clear();
            return false;
        }
    }

    private static JsonElement? ExtractCart(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (root.TryGetProperty("cart", out var nest) && nest.ValueKind == JsonValueKind.Object)
        {
            if (!string.IsNullOrEmpty(CartDisplayHelper.TryCartId(nest)))
                return nest.Clone();
        }

        if (!string.IsNullOrEmpty(CartDisplayHelper.TryCartId(root)))
            return root.Clone();

        return null;
    }

    /// <summary>404 «корзина не найдена» — как _api_error_cart_not_found.</summary>
    public static bool LooksLikeStaleCart(ApiException ex)
    {
        if (ex.StatusCode != 404)
            return false;

        var blob = ex.Message.ToLowerInvariant();
        if (ex.Payload is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } p)
            blob += " " + p.GetRawText().ToLowerInvariant();

        if (blob.Contains("product") && !blob.Contains("cart"))
            return false;
        return blob.Contains("no cart")
               || (blob.Contains("cart") && blob.Contains("match"))
               || (blob.Contains("cart") && blob.Contains("not found"));
    }
}
