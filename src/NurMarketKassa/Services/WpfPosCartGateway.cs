using System.Net.Http;

using System.Text.Json;

using NurMarketKassa.Core.Contracts;

using NurMarketKassa.Core.Domain;

using NurMarketKassa.Interfaces;

using NurMarketKassa.Models.Pos;



namespace NurMarketKassa.Services;



public sealed class WpfPosCartGateway : IPosCartGateway

{

    private readonly ICartService _cart;



    public WpfPosCartGateway(ICartService cart) => _cart = cart;



    public bool CanRefresh => _cart.CanRefresh;



    public string? CartId => _cart.CartId;



    public async Task<bool> EnsureSaleSessionAsync(CancellationToken cancellationToken = default)

    {

        if (_cart.CanRefresh)

            return true;



        if (OfflineModeHelper.UseLocalOperations)

        {

            LocalCartService.StartNewLocalCart(_cart);

            return true;

        }



        await TryStartNewSaleAsync(cancellationToken).ConfigureAwait(false);

        return _cart.CanRefresh;

    }



    public Task<bool> ScanBarcodeAsync(string barcode, string? quantity, CancellationToken cancellationToken = default)

    {

        if (OfflineModeHelper.UseLocalOperations)

            return ScanBarcodeOfflineAsync(barcode, quantity);



        return ExecuteWithStaleCartRetryAsync(

            () => App.SalesApi.PosScanAsync(_cart.CartId!, barcode, quantity, cancellationToken),

            cancellationToken);

    }



    public Task<bool> AddProductAsync(string productId, string quantity, CancellationToken cancellationToken = default)

    {

        if (OfflineModeHelper.UseLocalOperations)

            return AddProductOfflineAsync(productId, quantity);



        return ExecuteWithStaleCartRetryAsync(

            () => App.SalesApi.PosAddItemAsync(_cart.CartId!, productId, quantity, ct: cancellationToken),

            cancellationToken);

    }



    public IReadOnlyList<CartLineDto> GetCurrentLines()

    {

        if (!_cart.HasCart)

            return Array.Empty<CartLineDto>();



        return CartDisplayHelper.EnumerateItems(_cart.Root)

            .Select(it =>

            {

                var productId = CartDisplayHelper.TryProductId(it);

                var qty = CartDisplayHelper.LineQuantity(it);

                return string.IsNullOrEmpty(productId) ? null : new CartLineDto(productId, qty);

            })

            .Where(line => line != null)

            .Cast<CartLineDto>()

            .ToList();

    }



    private Task<bool> ScanBarcodeOfflineAsync(string barcode, string? quantity)

    {

        var tile = LocalProductRepository.Instance.TryGetTileByBarcode(barcode.Trim());

        if (tile == null)

            throw new CartOperationException($"Штрихкод {barcode} не найден в локальном каталоге.");



        LocalCartService.AddProduct(_cart, tile, quantity);

        return Task.FromResult(true);

    }



    private Task<bool> AddProductOfflineAsync(string productId, string quantity)

    {

        var tile = LocalProductRepository.Instance.TryGetTileById(productId);

        if (tile == null)

            throw new CartOperationException("Товар не найден в локальном каталоге.");



        LocalCartService.AddProduct(_cart, tile, quantity);

        return Task.FromResult(true);

    }



    private async Task<bool> ExecuteWithStaleCartRetryAsync(

        Func<Task<JsonElement>> apiCall,

        CancellationToken cancellationToken)

    {

        if (!_cart.CanRefresh || string.IsNullOrEmpty(_cart.CartId))

            throw new CartOperationException("Чек не открыт.");



        if (_cart.IsLocalOffline)

            throw new CartOperationException("Локальный офлайн-чек нельзя синхронизировать с сервером в офлайне.");



        for (var attempt = 0; attempt < 2; attempt++)

        {

            try

            {

                var resp = await apiCall().ConfigureAwait(false);

                if (!CartResponseHelper.TryUpdateCartSession(resp, _cart))

                    await ReloadCartAsync(cancellationToken).ConfigureAwait(false);

                else

                    await ReloadCartAsync(cancellationToken).ConfigureAwait(false);



                return true;

            }

            catch (ApiException ex) when (attempt == 0 && CartResponseHelper.LooksLikeStaleCart(ex))

            {

                try

                {

                    await TryStartNewSaleAsync(cancellationToken).ConfigureAwait(false);

                }

                catch (ApiException rex)

                {

                    throw new CartOperationException(PosErrorMessages.UserMessageForCatalogOrScan(rex));

                }

            }

            catch (ApiException ex)

            {

                throw new CartOperationException(PosErrorMessages.UserMessageForCatalogOrScan(ex));

            }

            catch (HttpRequestException ex)

            {

                throw new CartOperationException(

                    string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message);

            }

            catch (TaskCanceledException)

            {

                throw new CartOperationException("Таймаут запроса (проверьте сеть).");

            }

        }



        return false;

    }



    private async Task TryStartNewSaleAsync(CancellationToken cancellationToken)

    {

        var cb = await EnsurePosCashboxIdAsync(cancellationToken).ConfigureAwait(false);

        await CartSaleSessionHelper.StartNewSaleAsync(App.SalesApi, _cart, cb, cancellationToken).ConfigureAwait(false);

    }



    private async Task ReloadCartAsync(CancellationToken cancellationToken)

    {

        if (!_cart.CanRefresh || _cart.IsLocalOffline)

            return;



        var cart = await App.SalesApi.PosCartGetAsync(_cart.CartId!, cancellationToken).ConfigureAwait(false);

        if (cart.ValueKind != JsonValueKind.Object || !CartResponseHelper.TryUpdateCartSession(cart, _cart))
            _cart.Clear();

    }



    private static async Task<string?> EnsurePosCashboxIdAsync(CancellationToken cancellationToken)

    {

        var cb = App.PosCashboxId;

        if (!string.IsNullOrWhiteSpace(cb))

            return cb;



        if (OfflineModeHelper.UseLocalOperations)

            return App.PosCashboxId ?? "offline-cashbox";



        var rawList = await App.ShiftApi.ConstructionCashboxesListAsync(cancellationToken).ConfigureAwait(false);

        if (CartDisplayHelper.TryFirstCashbox(rawList, out var id, out var displayName))

        {

            cb = id;

            App.PosCashboxId = id;

            App.PosCashboxDisplayName = displayName;

        }



        return cb;

    }

}


