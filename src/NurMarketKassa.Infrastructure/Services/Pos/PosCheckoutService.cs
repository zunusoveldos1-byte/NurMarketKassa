using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using MediatR;
using NurMarketKassa.Core.Application.Notifications;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Core.Domain;
using NurMarketKassa.Interfaces;
using NurMarketKassa.Services.Api;
using NurMarketKassa.Services.Hardware;

namespace NurMarketKassa.Services;

/// <summary>
/// Общая реализация оплаты POS: онлайн checkout, офлайн-очередь, печать и новый чек.
/// </summary>
public sealed class PosCheckoutService : IPosCheckoutService
{
    private readonly ICartService _cart;
    private readonly ISalesApiService _salesApi;
    private readonly IShiftStateService _shiftStateService;
    private readonly IReceiptPrinterService _receiptPrinter;
    private readonly IMediator _mediator;

    public PosCheckoutService(
        ICartService cart,
        ISalesApiService salesApi,
        IShiftStateService shiftStateService,
        IReceiptPrinterService receiptPrinter,
        IMediator mediator)
    {
        _cart = cart;
        _salesApi = salesApi;
        _shiftStateService = shiftStateService;
        _receiptPrinter = receiptPrinter;
        _mediator = mediator;
    }

    public async Task PrepareCartForCheckoutAsync(CancellationToken cancellationToken = default)
    {
        if (!_cart.HasCart || _cart.LineCount == 0)
            throw new ApiException("Добавьте товары в корзину.", 400);

        if (OfflineModeHelper.UseLocalOperations || _cart.IsLocalOffline)
            return;

        if (_cart.IsStaging || ! _cart.CanRefresh)
        {
            await StagingCartService.MaterializeSnapshotOnServerAsync(
                _salesApi,
                _cart,
                PosApp.PosCashboxId,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> ApplyOrderDiscountAsync(
        Dictionary<string, string> discountBody,
        CancellationToken cancellationToken = default)
    {
        if (discountBody.Count == 0)
            return true;

        if (OfflineModeHelper.UseLocalOperations || _cart.IsLocalOffline || string.IsNullOrWhiteSpace(_cart.CartId))
            return true;

        try
        {
            await _salesApi
                .PosCartPatchAsync(_cart.CartId!, discountBody, cancellationToken)
                .ConfigureAwait(false);
            _cart.SetCart(await _salesApi.PosCartGetAsync(_cart.CartId!, cancellationToken).ConfigureAwait(false));
            return true;
        }
        catch (Exception ex)
        {
            PosLogger.Log($"Checkout discount failed: {ex.Message}", "PAYMENT");
            return false;
        }
    }

    public async Task<PosCheckoutResult> CheckoutAsync(
        PosCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_cart.HasCart || _cart.LineCount == 0)
            return PosCheckoutResult.Failed("Добавьте товары в корзину.");

        var cartJsonSnapshot = _cart.GetRawText();
        var total = CartTotalsCalculator.Calculate(_cart.Root).TotalDue;

        try
        {
            await PrepareCartForCheckoutAsync(cancellationToken).ConfigureAwait(false);

            if (request.OrderDiscountBody != null
                && !await ApplyOrderDiscountAsync(request.OrderDiscountBody, cancellationToken).ConfigureAwait(false))
            {
                return PosCheckoutResult.Failed(PaymentErrorMessages.DiscountFailure);
            }

            if (OfflineModeHelper.UseLocalOperations || _cart.IsLocalOffline)
                return CompleteOfflineCheckout(request, cartJsonSnapshot, total);

            return await CompleteOnlineCheckoutAsync(request, cartJsonSnapshot, total, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            return PosCheckoutResult.Failed(PaymentErrorMessages.ForCashier(ex));
        }
        catch (HttpRequestException ex)
        {
            PosLogger.Log($"Checkout network error, saving offline: {ex.Message}", "PAYMENT");
            return CompleteOfflineCheckout(request, cartJsonSnapshot, total, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return CompleteOfflineCheckout(request, cartJsonSnapshot, total, "Таймаут оплаты или потеря сети.");
        }
    }

    public async Task<string?> RestartSaleSessionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _shiftStateService.RefreshAsync(cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(PosApp.ActiveShiftId))
            {
                _cart.ResetForNewReceipt();
                return "Новый чек не открыт: смена не открыта. Откройте смену и нажмите «Новый чек».";
            }

            if (OfflineModeHelper.UseLocalOperations)
            {
                LocalCartService.StartNewLocalCart(_cart);
                return null;
            }

            var serverCart = await _salesApi
                .PosSalesStartAsync(PosApp.PosCashboxId, cancellationToken)
                .ConfigureAwait(false);
            _cart.SetCart(serverCart);
            return null;
        }
        catch (ApiException ex) when (OfflineModeHelper.CanOperateWithoutServer)
        {
            LocalCartService.StartNewLocalCart(_cart);
            return null;
        }
        catch (HttpRequestException ex) when (OfflineModeHelper.CanOperateWithoutServer)
        {
            LocalCartService.StartNewLocalCart(_cart);
            PosLogger.Log($"Restart sale offline after network error: {ex.Message}", "PAYMENT");
            return null;
        }
        catch (Exception ex)
        {
            PosLogger.Log($"Restart sale failed: {ex.Message}", "PAYMENT");
            return ex.Message;
        }
    }

    private PosCheckoutResult CompleteOfflineCheckout(
        PosCheckoutRequest request,
        string cartJsonSnapshot,
        double total,
        string? reason = null)
    {
        var entry = new OfflineSaleEntry
        {
            PaymentMethod = request.PaymentMethod ?? "",
            CashReceived = request.CashReceived,
            CartJson = cartJsonSnapshot,
            CartId = _cart.CartId,
            ShiftId = PosApp.ActiveShiftId,
            BranchId = PosApp.AuthApi.ActiveBranchId,
            CashboxId = PosApp.PosCashboxId,
        };

        OfflinePendingSalesStore.Append(entry);
        ApplyOfflineStockDecrement(cartJsonSnapshot);

        if (request.PrintReceipt)
            TryPrintReceipt(cartJsonSnapshot, request.PaymentMethod, request.CashReceived, offlineNote: "ОФФЛАЙН (ожидает выгрузку)");

        _ = RestartSaleSessionAsync();

        var info = reason != null
            ? $"Оплата сохранена локально ({reason}). В очереди: {OfflinePendingSalesStore.PendingCount}."
            : $"Оплата сохранена локально. В очереди: {OfflinePendingSalesStore.PendingCount}.";

        return PosCheckoutResult.OfflineSaved(total, cartJsonSnapshot, info);
    }

    private async Task<PosCheckoutResult> CompleteOnlineCheckoutAsync(
        PosCheckoutRequest request,
        string cartJsonSnapshot,
        double total,
        CancellationToken cancellationToken)
    {
        var cartId = _cart.CartId;
        if (string.IsNullOrWhiteSpace(cartId))
            return PosCheckoutResult.Failed("Корзина не привязана к серверу. Начните продажу заново.");

        var body = BuildCheckoutRequestBody(request.PaymentMethod, request.CashReceived, request.PrintReceipt);
        var checkoutIds = CartDisplayHelper.CollectCheckoutTargetIds(_cart.Root, cartId);

        var checkoutResponse = await _salesApi
            .PosCheckoutAsync(checkoutIds, body, cancellationToken)
            .ConfigureAwait(false);

        CheckoutResponseHelper.FormatSuccess(checkoutResponse);

        var saleId = CheckoutResponseHelper.TrySaleId(checkoutResponse) ?? cartId;
        var cartSnapshot = _cart.Root.Clone();
        await PublishSaleFinalizedAsync(saleId, cartSnapshot).ConfigureAwait(false);
        _ = Task.Run(() => StockSyncService.RefreshSoldItemsStockAsync(cartSnapshot, CancellationToken.None));

        PosApp.AuditDb.LogSale(saleId, total, request.PaymentMethod ?? "", PosApp.CurrentUserId);

        if (request.PrintReceipt)
            TryPrintReceipt(cartJsonSnapshot, request.PaymentMethod, request.CashReceived, checkoutResponse: checkoutResponse);

        await RestartSaleSessionAsync(cancellationToken).ConfigureAwait(false);

        return PosCheckoutResult.Succeeded(total, cartJsonSnapshot, checkoutResponse);
    }

    private async Task PublishSaleFinalizedAsync(string saleId, JsonElement cartSnapshot)
    {
        var saleLines = CartDisplayHelper.EnumerateItems(cartSnapshot)
            .Select(it =>
            {
                var productId = CartDisplayHelper.TryProductId(it);
                var qty = CartDisplayHelper.LineQuantity(it);
                return string.IsNullOrEmpty(productId) ? null : new CartLineDto(productId, qty);
            })
            .Where(line => line != null)
            .Cast<CartLineDto>()
            .ToList();

        if (saleLines.Count > 0)
        {
            await _mediator.Publish(new SaleFinalizedNotification(saleId, saleLines), CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> BuildCheckoutRequestBody(
        string paymentMethod,
        string cashReceived,
        bool printReceipt)
    {
        var body = new Dictionary<string, string>
        {
            ["payment_method"] = paymentMethod ?? "",
            ["print_receipt"] = printReceipt ? "true" : "false",
            ["cash_received"] = cashReceived ?? "",
        };

        if (!string.IsNullOrWhiteSpace(PosApp.PosCashboxId))
            body["cashbox_id"] = PosApp.PosCashboxId.Trim();

        var shiftId = PosApp.ActiveShiftId;
        if (!string.IsNullOrWhiteSpace(shiftId)
            && !shiftId.StartsWith("offline-", StringComparison.OrdinalIgnoreCase))
            body["shift_id"] = shiftId.Trim();

        return body;
    }

    private static void ApplyOfflineStockDecrement(string cartJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(cartJson) ? "{}" : cartJson);
            foreach (var item in CartDisplayHelper.EnumerateItems(doc.RootElement))
            {
                var productId = CartDisplayHelper.TryProductId(item);
                if (string.IsNullOrEmpty(productId))
                    continue;

                var soldQty = CartDisplayHelper.LineQuantity(item);
                if (soldQty <= 0)
                    continue;

                var tile = CatalogCacheService.Products.FirstOrDefault(p =>
                    string.Equals(p.Id, productId, StringComparison.OrdinalIgnoreCase));
                if (tile == null)
                    continue;

                tile.Quantity = Math.Max(0, tile.Quantity - soldQty);
                LocalProductRepository.Instance.UpdateStock(productId, tile.Quantity, tile.MustWeigh);
            }
        }
        catch
        {
            /* остатки подтянутся при синхронизации каталога */
        }
    }

    private static void TryPrintReceipt(
        string cartJson,
        string? paymentMethod,
        string? cashReceived,
        string? offlineNote = null,
        JsonElement? checkoutResponse = null)
    {
        try
        {
            if (!HardwareModeHelper.IsPrinterPortConfigured())
            {
                PosLogger.Log("Print skipped: printer port not configured.", "PRINTER");
                return;
            }

            ReceiptPrintService.PrintReceipt(
                cartJson,
                offlineNote: offlineNote,
                paymentMethodKey: paymentMethod,
                cashReceived: cashReceived);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"Print failed: {ex.Message}", "PRINTER");
        }
    }
}
