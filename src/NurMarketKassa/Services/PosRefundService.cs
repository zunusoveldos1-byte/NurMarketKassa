using System.Globalization;
using System.Text.Json;
using NurMarketKassa.Services.Api;

namespace NurMarketKassa.Services;

/// <summary>Возврат по API Nur CRM: cart-item-deletions, carts/items или чек возврата (sales/start).</summary>
public static class PosRefundService
{
    public static async Task RefundWholeSaleAsync(
        ISalesApiService api,
        string saleId,
        string reason,
        string? cashboxId,
        CancellationToken ct = default)
    {
        try
        {
            await api.PosReturnWholeSaleAsync(saleId, reason, ct).ConfigureAwait(false);
            PosLogger.Log($"Возврат чека {saleId}: DELETE sales/{{id}}.", "REFUND");
            return;
        }
        catch (ApiException ex) when (ex.StatusCode is 400 or 404 or 405 or 409)
        {
            PosLogger.Log($"Полный DELETE продажи не выполнен ({ex.StatusCode}), построчный возврат: {ex.Message}", "REFUND");
        }

        var sale = await api.PosSaleGetAsync(saleId, ct).ConfigureAwait(false);
        var lines = BuildLineRequests(sale);
        if (lines.Count == 0)
            throw new ApiException("В чеке нет позиций для возврата.", 400);

        await RefundLinesAsync(api, saleId, lines, reason, cashboxId, ct).ConfigureAwait(false);
    }

    public static async Task RefundLinesAsync(
        ISalesApiService api,
        string saleId,
        IReadOnlyList<PosRefundLineRequest> lines,
        string reason,
        string? cashboxId,
        CancellationToken ct = default)
    {
        if (lines.Count == 0)
            throw new ApiException("Не выбраны позиции для возврата.", 400);

        var sale = await api.PosSaleGetAsync(saleId, ct).ConfigureAwait(false);
        var cartId = await ResolveVerifiedCartIdAsync(api, sale, ct).ConfigureAwait(false);
        var enriched = EnrichLinesFromSale(sale, lines);

        var errors = new List<string>();
        var succeeded = 0;
        var pending = new List<PosRefundLineRequest>();

        foreach (var line in enriched)
        {
            if (await TryDeletionReturnAsync(api, saleId, cartId, line, reason, ct).ConfigureAwait(false))
            {
                succeeded++;
                continue;
            }

            if (!string.IsNullOrEmpty(cartId))
            {
                try
                {
                    await api.PosReturnCartLineAsync(cartId, line, reason, ct).ConfigureAwait(false);
                    succeeded++;
                    continue;
                }
                catch (ApiException ex) when (CartResponseHelper.LooksLikeStaleCart(ex))
                {
                    PosLogger.Log($"Корзина {cartId} недоступна для возврата {line.Title}: {ex.Message}", "REFUND");
                    cartId = null;
                }
                catch (ApiException ex)
                {
                    PosLogger.Log($"carts/items возврат {line.Title}: {ex.Message}", "REFUND");
                }
            }

            pending.Add(line);
        }

        if (pending.Count > 0)
        {
            try
            {
                await RefundViaNewSaleCartAsync(api, saleId, pending, reason, cashboxId, ct).ConfigureAwait(false);
                succeeded += pending.Count;
            }
            catch (ApiException ex)
            {
                foreach (var line in pending)
                    errors.Add($"{line.Title}: {ex.Message}");
            }
        }

        if (succeeded == enriched.Count)
        {
            PosLogger.Log($"Возврат чека {saleId}: {succeeded} поз.", "REFUND");
            return;
        }

        if (succeeded > 0)
        {
            throw new ApiException(
                $"Возвращено позиций: {succeeded} из {enriched.Count}.\n\n" + string.Join("\n", errors),
                502);
        }

        throw new ApiException(errors.Count > 0
            ? string.Join("\n", errors)
            : "Не удалось оформить возврат. Проверьте, что чек оплачен и смена открыта.", 502);
    }

    public static List<PosRefundLineRequest> BuildLineRequests(JsonElement sale)
    {
        var list = new List<PosRefundLineRequest>();
        foreach (var lineItem in CartDisplayHelper.EnumerateSaleLineItems(sale))
        {
            if (CartDisplayHelper.LineLooksFullyReturned(lineItem))
                continue;

            var lineId = CartDisplayHelper.TryRefundLineId(lineItem);
            if (string.IsNullOrEmpty(lineId))
                continue;

            var originalQty = CartDisplayHelper.LineQuantity(lineItem);
            var qty = CartDisplayHelper.RefundableQuantity(lineItem);
            if (qty <= 0)
                continue;

            list.Add(new PosRefundLineRequest
            {
                LineId = lineId,
                ProductId = CartDisplayHelper.TryProductId(lineItem),
                Title = CartDisplayHelper.ItemName(lineItem),
                Quantity = qty,
                OriginalQuantity = originalQty,
                UnitPrice = CartDisplayHelper.FormatMoney(CartDisplayHelper.UnitPrice(lineItem)),
            });
        }

        return list;
    }

    private static async Task<bool> TryDeletionReturnAsync(
        ISalesApiService api,
        string saleId,
        string? cartId,
        PosRefundLineRequest line,
        string reason,
        CancellationToken ct) =>
        await api.TryPosCartItemDeletionReturnAsync(saleId, cartId, line, reason, ct).ConfigureAwait(false);

    private static async Task RefundViaNewSaleCartAsync(
        ISalesApiService api,
        string saleId,
        IReadOnlyList<PosRefundLineRequest> lines,
        string reason,
        string? cashboxId,
        CancellationToken ct)
    {
        var refundCartId = await StartRefundSaleCartAsync(api, saleId, reason, cashboxId, ct).ConfigureAwait(false);

        foreach (var line in lines)
            await AddRefundLineAsync(api, refundCartId, saleId, line, reason, ct).ConfigureAwait(false);

        var checkoutBodies = new List<Dictionary<string, string>>
        {
            new()
            {
                ["payment_method"] = "cash",
                ["cash_received"] = "0",
                ["is_refund"] = "true",
                ["original_sale_id"] = saleId,
                ["refund_reason"] = reason,
            },
            new()
            {
                ["payment_method"] = "cash",
                ["cash_received"] = "0.00",
                ["refund_reason"] = reason,
            },
            new() { ["payment_method"] = "cash", ["cash_received"] = "0" },
        };

        ApiException? last = null;
        foreach (var body in checkoutBodies)
        {
            try
            {
                await api.PosCheckoutAsync(refundCartId, body, ct).ConfigureAwait(false);
                PosLogger.Log($"Возврат чека {saleId}: checkout корзины {refundCartId}.", "REFUND");
                return;
            }
            catch (ApiException ex)
            {
                last = ex;
                if (ex.StatusCode == 400)
                    continue;
                throw;
            }
        }

        throw last ?? new ApiException("Не удалось завершить возврат (checkout).", 502);
    }

    private static async Task<string> StartRefundSaleCartAsync(
        ISalesApiService api,
        string saleId,
        string reason,
        string? cashboxId,
        CancellationToken ct)
    {
        var payloads = new List<Dictionary<string, string>>();
        if (!string.IsNullOrWhiteSpace(cashboxId))
        {
            payloads.Add(new Dictionary<string, string>
            {
                ["cashbox_id"] = cashboxId.Trim(),
                ["original_sale_id"] = saleId,
                ["is_refund"] = "true",
                ["refund_reason"] = reason,
            });
            payloads.Add(new Dictionary<string, string>
            {
                ["cashbox_id"] = cashboxId.Trim(),
                ["sale_id"] = saleId,
                ["is_refund"] = "true",
                ["refund_reason"] = reason,
            });
        }

        payloads.Add(new Dictionary<string, string>
        {
            ["original_sale_id"] = saleId,
            ["is_refund"] = "true",
            ["refund_reason"] = reason,
        });
        payloads.Add(new Dictionary<string, string>
        {
            ["sale_id"] = saleId,
            ["is_refund"] = "true",
            ["refund_reason"] = reason,
        });

        if (!string.IsNullOrWhiteSpace(cashboxId))
            payloads.Add(new Dictionary<string, string> { ["cashbox_id"] = cashboxId.Trim() });

        ApiException? last = null;
        foreach (var body in payloads)
        {
            try
            {
                var start = await api.PosSalesStartAsync(body, ct).ConfigureAwait(false);
                var cartId = ExtractCartIdFromStartResponse(start);
                if (string.IsNullOrEmpty(cartId))
                    continue;

                var session = new CartService();
                session.SetCart(start);
                await CartSaleSessionHelper.EnsureServerCartEmptyAsync(api, session, ct).ConfigureAwait(false);
                return session.CartId ?? cartId;
            }
            catch (ApiException ex)
            {
                last = ex;
                if (ex.StatusCode is 400 or 404 or 409)
                    continue;
                throw;
            }
        }

        throw last ?? new ApiException("Не удалось открыть чек возврата на сервере.", 502);
    }

    private static async Task AddRefundLineAsync(
        ISalesApiService api,
        string refundCartId,
        string saleId,
        PosRefundLineRequest line,
        string reason,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(line.ProductId))
            throw new ApiException($"Нет product_id для «{line.Title}».", 400);

        var qty = FormatQty(line.Quantity);
        var negQty = FormatQty(-line.Quantity);
        var bodies = new List<Dictionary<string, string>>
        {
            new()
            {
                ["product_id"] = line.ProductId,
                ["quantity"] = qty,
                ["original_sale_id"] = saleId,
                ["sale_line_id"] = line.LineId,
                ["cart_item_id"] = line.LineId,
                ["refund_reason"] = reason,
                ["is_return"] = "true",
            },
            new()
            {
                ["product_id"] = line.ProductId,
                ["quantity"] = qty,
                ["original_sale_id"] = saleId,
                ["refund_reason"] = reason,
            },
            new()
            {
                ["product_id"] = line.ProductId,
                ["quantity"] = negQty,
                ["refund_reason"] = reason,
                ["is_return"] = "true",
            },
            new()
            {
                ["product_id"] = line.ProductId,
                ["quantity"] = qty,
                ["refund_reason"] = reason,
            },
        };

        if (!string.IsNullOrWhiteSpace(line.UnitPrice))
            bodies[0]["unit_price"] = line.UnitPrice;

        ApiException? last = null;
        foreach (var body in bodies)
        {
            try
            {
                await api.PosAddItemRawAsync(refundCartId, body, ct).ConfigureAwait(false);
                return;
            }
            catch (ApiException ex)
            {
                last = ex;
                if (ex.StatusCode == 400)
                    continue;
                throw;
            }
        }

        throw last ?? new ApiException($"Не удалось добавить «{line.Title}» в чек возврата.", 502);
    }

    private static string? ExtractCartIdFromStartResponse(JsonElement start) =>
        CartDisplayHelper.TryCartId(start)
        ?? CartDisplayHelper.TryResolveCartIdFromSale(start);

    private static List<PosRefundLineRequest> EnrichLinesFromSale(
        JsonElement sale,
        IReadOnlyList<PosRefundLineRequest> lines)
    {
        var result = new List<PosRefundLineRequest>(lines.Count);
        foreach (var line in lines)
        {
            var lineId = line.LineId;
            var freshId = CartDisplayHelper.TryRefundLineIdForProduct(sale, line.ProductId);
            if (!string.IsNullOrEmpty(freshId))
                lineId = freshId;

            result.Add(string.Equals(lineId, line.LineId, StringComparison.OrdinalIgnoreCase)
                ? line
                : new PosRefundLineRequest
                {
                    LineId = lineId,
                    ProductId = line.ProductId,
                    Title = line.Title,
                    Quantity = line.Quantity,
                    OriginalQuantity = line.OriginalQuantity,
                    UnitPrice = line.UnitPrice,
                });
        }

        return result;
    }

    private static async Task<string?> ResolveVerifiedCartIdAsync(
        ISalesApiService api,
        JsonElement sale,
        CancellationToken ct)
    {
        var candidate = CartDisplayHelper.TryResolveCartIdFromSale(sale);
        if (string.IsNullOrEmpty(candidate))
            return null;

        try
        {
            await api.PosCartGetAsync(candidate, ct).ConfigureAwait(false);
            return candidate;
        }
        catch (ApiException ex) when (ex.StatusCode is 400 or 404)
        {
            PosLogger.Log($"cart_id {candidate} из продажи недоступен: {ex.Message}", "REFUND");
            return null;
        }
    }

    private static string FormatQty(double q)
    {
        var s = q.ToString("0.####", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        return string.IsNullOrEmpty(s) ? "0" : s;
    }
}
