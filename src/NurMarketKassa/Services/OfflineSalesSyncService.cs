using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Services.Api;

namespace NurMarketKassa.Services;

public sealed class OfflineSalesSyncService : IDisposable
{
    private readonly ISalesApiService _sales;
    private readonly IAuthApiService _auth;
    private readonly ISyncConflictResolver _syncConflictResolver;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public OfflineSalesSyncService(
        ISalesApiService sales,
        IAuthApiService auth,
        ISyncConflictResolver syncConflictResolver)
    {
        _sales = sales;
        _auth = auth;
        _syncConflictResolver = syncConflictResolver;
    }

    public event EventHandler? StateChanged;

    public bool IsOnline { get; private set; }

    public bool IsSyncInProgress { get; private set; }

    public string StatusText { get; private set; } = "Проверка связи…";

    public void Start()
    {
        if (_loopTask != null)
            return;

        foreach (var entry in OfflinePendingSalesStore.LoadAll())
        {
            if (!string.Equals(entry.Status, OfflineSaleEntry.Syncing, StringComparison.OrdinalIgnoreCase))
                continue;

            OfflinePendingSalesStore.MarkFailed(entry.Id, "Синхронизация была прервана перезапуском кассы.", retryable: true);
        }

        _loopTask = RunLoopAsync(_cts.Token);
    }

    public async Task ProbeNowAsync(CancellationToken ct = default)
    {
        IsOnline = await _auth.CanReachApiAsync(ct).ConfigureAwait(false);
        UpdateStatusText();
        RaiseStateChanged();
    }

    public async Task TriggerSyncNowAsync(CancellationToken ct = default)
    {
        await SyncPendingAsync(ct).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(8));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProbeNowAsync(ct).ConfigureAwait(false);
                if (IsOnline && OfflinePendingSalesStore.PendingCount > 0)
                    await SyncPendingAsync(ct).ConfigureAwait(false);
                await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusText = "Синхронизация: " + ex.Message;
                RaiseStateChanged();
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task SyncPendingAsync(CancellationToken ct)
    {
        if (!await _syncGate.WaitAsync(0, ct).ConfigureAwait(false))
            return;

        try
        {
            IsSyncInProgress = true;
            UpdateStatusText();
            RaiseStateChanged();

            foreach (var entry in OfflinePendingSalesStore.LoadPendingForSync())
            {
                ct.ThrowIfCancellationRequested();
                if (!IsOnline)
                    break;

                OfflinePendingSalesStore.MarkSyncing(entry.Id);
                UpdateStatusText();
                RaiseStateChanged();

                try
                {
                    var saleId = await ReplayOfflineSaleAsync(entry, ct).ConfigureAwait(false);
                    OfflinePendingSalesStore.MarkSynced(entry.Id, saleId);
                    OfflinePendingSalesStore.RemoveSynced(entry.Id);
                }
                catch (HttpRequestException ex)
                {
                    IsOnline = false;
                    OfflinePendingSalesStore.MarkFailed(entry.Id, ex.Message, retryable: true);
                    break;
                }
                catch (TaskCanceledException ex)
                {
                    IsOnline = false;
                    OfflinePendingSalesStore.MarkFailed(entry.Id, string.IsNullOrWhiteSpace(ex.Message) ? "Таймаут сети." : ex.Message, retryable: true);
                    break;
                }
                catch (ApiException ex)
                {
                    OfflinePendingSalesStore.MarkFailed(entry.Id, ex.Message, retryable: false);
                }
                catch (JsonException ex)
                {
                    OfflinePendingSalesStore.MarkFailed(entry.Id, ex.Message, retryable: false);
                }

                UpdateStatusText();
                RaiseStateChanged();
            }

            if (IsOnline)
            {
                try
                {
                    await CatalogCacheService.RefreshFromApiAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    /* каталог обновится при следующем цикле */
                }
            }
        }
        finally
        {
            IsSyncInProgress = false;
            UpdateStatusText();
            RaiseStateChanged();
            _syncGate.Release();
        }
    }

    private async Task<string?> ReplayOfflineSaleAsync(OfflineSaleEntry entry, CancellationToken ct)
    {
        var start = await _sales.PosSalesStartAsync(entry.CashboxId, ct).ConfigureAwait(false);
        var cartId = CartDisplayHelper.TryCartId(start);
        if (string.IsNullOrEmpty(cartId))
            throw new ApiException("Сервер не вернул cart_id для синхронизации офлайн-чека.", 500);

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(entry.CartJson) ? "{}" : entry.CartJson);
        var root = doc.RootElement;

        foreach (var item in CartDisplayHelper.EnumerateItems(root))
        {
            var productId = CartDisplayHelper.TryProductId(item);
            if (string.IsNullOrEmpty(productId))
                continue;

            var weighed = CartDisplayHelper.LineMustWeigh(item);
            var qty = FormatQuantityForApi(CartDisplayHelper.LineQuantity(item), weighed);
            var price = CartDisplayHelper.FormatMoney(CartDisplayHelper.UnitPrice(item));
            var discount = CartDisplayHelper.OptionalDiscountTotalParam(item);
            await _sales.PosAddItemAsync(cartId, productId, qty, price, discount, ct).ConfigureAwait(false);
        }

        ApplyOrderDiscountPatch(root, out var discountBody);
        if (discountBody.Count > 0)
            await _sales.PosCartPatchAsync(cartId, discountBody, ct).ConfigureAwait(false);

        var body = new Dictionary<string, string>
        {
            ["payment_method"] = entry.PaymentMethod ?? "",
            ["print_receipt"] = "false",
            ["cash_received"] = entry.CashReceived ?? "",
        };

        var result = await _sales.PosCheckoutAsync(cartId, body, ct).ConfigureAwait(false);

        foreach (var item in CartDisplayHelper.EnumerateItems(root))
        {
            var productId = CartDisplayHelper.TryProductId(item);
            if (string.IsNullOrEmpty(productId))
                continue;

            var soldQty = CartDisplayHelper.LineQuantity(item);
            if (soldQty <= 0)
                continue;

            try
            {
                await _syncConflictResolver.ResolveAndSyncStockAsync(
                    productId,
                    -soldQty,
                    SyncStrategy.Accumulate,
                    ct).ConfigureAwait(false);
            }
            catch
            {
                /* синхронизация остатков не должна отменять отправку чека */
            }
        }

        return CheckoutResponseHelper.TrySaleId(result);
    }

    private static void ApplyOrderDiscountPatch(JsonElement root, out Dictionary<string, string> body)
    {
        body = new Dictionary<string, string>();
        if (root.ValueKind != JsonValueKind.Object)
            return;

        if (root.TryGetProperty("order_discount_percent", out var pct))
        {
            var value = ReadScalar(pct);
            if (!string.IsNullOrWhiteSpace(value) && !OrderDiscountHelper.IsEmptyOrZeroLike(value))
            {
                body["order_discount_percent"] = OrderDiscountHelper.NormalizeDecimal(value);
                return;
            }
        }

        if (root.TryGetProperty("order_discount_total", out var total))
        {
            var value = ReadScalar(total);
            if (!string.IsNullOrWhiteSpace(value) && !OrderDiscountHelper.IsEmptyOrZeroLike(value))
                body["order_discount_total"] = OrderDiscountHelper.NormalizeDecimal(value);
        }
    }

    private static string ReadScalar(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString() ?? "",
            _ => "",
        };

    private static string FormatQuantityForApi(double quantity, bool weighed)
    {
        if (weighed)
        {
            var text = quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                .TrimEnd('0')
                .TrimEnd('.');
            return string.IsNullOrEmpty(text) ? "0" : text;
        }

        return Math.Round(quantity, 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void UpdateStatusText()
    {
        var pending = OfflinePendingSalesStore.PendingCount;
        if (IsSyncInProgress)
        {
            StatusText = pending > 0
                ? $"Синхронизация очереди: {pending} чек(ов)."
                : "Синхронизация завершается…";
            return;
        }

        if (!IsOnline)
        {
            StatusText = pending > 0
                ? $"Оффлайн. В очереди {pending} чек(ов)."
                : "Оффлайн. Продажи будут сохранены локально.";
            return;
        }

        StatusText = pending > 0
            ? $"Онлайн. Ожидают синхронизации: {pending}."
            : "Онлайн. Очередь синхронизации пуста.";
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            /* ignore */
        }

        _cts.Dispose();
        _syncGate.Dispose();
    }
}
