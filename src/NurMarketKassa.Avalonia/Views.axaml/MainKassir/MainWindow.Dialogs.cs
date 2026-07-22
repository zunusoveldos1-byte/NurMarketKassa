using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using NurMarketKassa.AvaloniaHost.Services;
using NurMarketKassa.AvaloniaHost.Views.Dialogs;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Interfaces;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Hardware;

namespace NurMarketKassa.AvaloniaHost.Views.MainKassir;

public partial class MainWindow
{
    private ICartService? _cartService;
    private IDeferredCartService? _deferredCartService;

    private void WireDialogBridge()
    {
        _hostBridge.AddProductFromCatalog = AddProductFromCatalogAsync;
        _hostBridge.OpenDeferredCarts = OpenDeferredCartsAsync;
        _hostBridge.OpenCashOperations = OpenCashOperationsAsync;
        _hostBridge.ApplyOrderDiscount = ApplyOrderDiscountAsync;
    }

    internal decimal? GetCurrentBalance() => _shiftCashBalance;

    internal async Task<bool> OpenShiftFromCoordinatorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await OpenShiftAsync().ConfigureAwait(true);
        return _session.IsShiftOpen;
    }

    internal Task OpenDeferredCartsAsync()
    {
        var dlg = new DeferredCartsDialog(new DeferredCartsDialogActions
        {
            MergeIntoCurrentAsync = MergeDeferredIntoCurrentAsync,
            OpenAsSeparateAsync = OpenDeferredAsSeparateAsync,
        });
        PosDialogHost.Show(dlg, this);
        _viewModel.Basket.RefreshFromCart();
        return Task.CompletedTask;
    }

    internal Task OpenCashOperationsAsync()
    {
        var dlg = App.GetRequiredService<CashOperationsDialog>();
        dlg.OpenShiftAction = async cash => await ApplyShiftOpenedAsync(cash).ConfigureAwait(true);
        dlg.CloseShiftAction = async cash => await ApplyShiftClosedAsync(cash).ConfigureAwait(true);
        PosDialogHost.Show(dlg, this);
        return Task.CompletedTask;
    }

    internal async Task AddProductFromCatalogAsync(CatalogProductTileVm vm)
    {
        if (!_session.IsShiftOpen)
        {
            await OpenShiftAsync().ConfigureAwait(true);
            if (!_session.IsShiftOpen)
                return;
        }

        var cart = ResolveCartService();
        double qtyToAdd;
        var mustWeigh = ProductUnitNormalizer.RequiresWeighing(vm);

        if (mustWeigh)
        {
            var scale = HardwareModeHelper.UsePhysicalScale()
                ? App.GetRequiredService<ScaleWeightProvider>().Scale
                : null;
            var dlg = new WeighedProductDialog(vm.Title, vm.PriceLine, scale);
            if (PosDialogHost.Show(dlg, this) != true || string.IsNullOrEmpty(dlg.QuantityNormalized))
                return;

            if (!double.TryParse(dlg.QuantityNormalized, NumberStyles.Any, CultureInfo.InvariantCulture, out qtyToAdd) || qtyToAdd <= 0)
                return;
        }
        else
        {
            qtyToAdd = ParseManualQuantity(_viewModel.Basket.ManualQuantity, false);
            if (qtyToAdd <= 0)
            {
                PosMessageBox.Show(this, "Укажите корректное количество.", "Количество",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (!StockAvailabilityService.CanAddQuantity(vm.Id, qtyToAdd, cart))
        {
            ShowNoStockBlocked(vm.Title, vm.Id);
            return;
        }

        _viewModel.Basket.ManualQuantity = mustWeigh
            ? qtyToAdd.ToString("0.###", CultureInfo.InvariantCulture)
            : qtyToAdd.ToString("0", CultureInfo.InvariantCulture);
        _viewModel.Basket.AddProductFromCatalog(vm);
    }

    internal Task ApplyOrderDiscountAsync()
    {
        var dlg = App.GetRequiredService<OrderDiscountDialog>();
        if (PosDialogHost.Show(dlg, this) != true)
            return Task.CompletedTask;

        if (dlg.ClearRequested)
        {
            _viewModel.Basket.CartMessage = "Скидка сброшена.";
            return Task.CompletedTask;
        }

        _viewModel.Basket.CartMessage = dlg.DiscountMode == "percent"
            ? $"Скидка {dlg.DiscountValue}% применена."
            : $"Скидка {dlg.DiscountValue} сом применена.";
        return Task.CompletedTask;
    }

    private async Task<bool> MergeDeferredIntoCurrentAsync(IReadOnlyList<DeferredCartEntry> entries)
    {
        if (entries.Count == 0)
            return false;

        if (!_session.IsShiftOpen)
        {
            await OpenShiftAsync().ConfigureAwait(true);
            if (!_session.IsShiftOpen)
                return false;
        }

        foreach (var entry in entries)
        {
            if (!await AddDeferredEntryItemsToActiveCartAsync(entry).ConfigureAwait(true))
                return false;

            DeferredCartsStore.RemoveIds(new[] { entry.Id });
        }

        _viewModel.Basket.RefreshFromCart();
        _viewModel.Basket.CartMessage = $"Позиции из {entries.Count} отложенных чеков добавлены в текущий чек.";
        return true;
    }

    private async Task<bool> OpenDeferredAsSeparateAsync(DeferredCartEntry entry)
    {
        if (!_session.IsShiftOpen)
        {
            await OpenShiftAsync().ConfigureAwait(true);
            if (!_session.IsShiftOpen)
                return false;
        }

        var cart = ResolveCartService();
        if (cart.HasCart && cart.LineCount > 0)
        {
            var deferResult = await ResolveDeferredCartService()
                .DeferCurrentCartAsync(startNewSale: false)
                .ConfigureAwait(true);
            if (!deferResult.IsSuccess)
            {
                PosMessageBox.Show(this, deferResult.ErrorMessage ?? "Не удалось сохранить текущий чек.",
                    "Отложенные", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        StagingCartService.StartEmpty(cart);
        OpenReceiptSnapshot.ApplyDeferredStaging(cart, entry.CartJson);
        DeferredCartsStore.RemoveIds(new[] { entry.Id });
        _viewModel.Basket.RefreshFromCart();
        _viewModel.Basket.CartMessage = $"Открыт отложенный чек «{entry.Label}».";
        return true;
    }

    private async Task<bool> AddDeferredEntryItemsToActiveCartAsync(
        DeferredCartEntry entry,
        bool applyOrderDiscount = false)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(entry.CartJson) ? "{}" : entry.CartJson);
        var root = doc.RootElement;
        var lines = CartDisplayHelper.EnumerateItems(root).ToList();
        if (lines.Count == 0)
            return true;

        var cart = ResolveCartService();
        if (!cart.HasCart)
            StagingCartService.StartEmpty(cart);

        foreach (var line in lines)
        {
            var productId = CartDisplayHelper.TryProductId(line);
            if (string.IsNullOrEmpty(productId))
                continue;

            var qty = CartDisplayHelper.LineQuantity(line);
            if (qty <= 0)
                continue;

            var product = ResolveCatalogProductForCartLine(line, productId);
            if (product == null)
                continue;

            cart.AddItem(product, qty);
        }

        if (applyOrderDiscount)
            ApplyDeferredOrderDiscount(cart, root);

        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    private static void ApplyDeferredOrderDiscount(ICartService cart, JsonElement cartRoot)
    {
        if (cartRoot.ValueKind != JsonValueKind.Object)
            return;

        var pct = cartRoot.TryGetProperty("order_discount_percent", out var p)
            ? FormatDiscountScalar(p)
            : "";
        var sum = cartRoot.TryGetProperty("order_discount_total", out var t)
            ? FormatDiscountMoney(t)
            : "";

        if (string.IsNullOrEmpty(pct) && string.IsNullOrEmpty(sum))
            return;

        ReceiptSnapshotCartEditor.PatchOrderDiscount(cart, pct, sum);
    }

    private static string FormatDiscountScalar(JsonElement value) =>
        JsonNumericReader.ToDouble(value).ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatDiscountMoney(JsonElement value) =>
        CartDisplayHelper.FormatMoney(JsonNumericReader.ToDouble(value));

    private static CatalogProductTileVm? ResolveCatalogProductForCartLine(JsonElement line, string productId)
    {
        var fromCache = LocalProductRepository.Instance.TryGetTileBySku(productId)
            ?? CatalogCacheService.Products.FirstOrDefault(p =>
                string.Equals(p.Id, productId, StringComparison.OrdinalIgnoreCase));
        if (fromCache != null)
            return fromCache;

        var title = CartDisplayHelper.ItemName(line);
        if (string.IsNullOrWhiteSpace(title))
            title = productId;

        var price = CartDisplayHelper.FormatMoney(CartDisplayHelper.UnitPrice(line));
        var mustWeigh = CartDisplayHelper.LineMustWeigh(line);
        return new CatalogProductTileVm(productId, title, price + " сом", mustWeigh);
    }

    private void ShowNoStockBlocked(string productName, string productId)
    {
        var warehouse = StockAvailabilityService.GetWarehouseQuantity(productId);
        var reserved = StockAvailabilityService.CalculateReservedQuantity(productId);
        var available = StockAvailabilityService.GetAvailableToAdd(productId, ResolveCartService());
        var reservedElsewhere = reserved > 1e-6 || (warehouse > 1e-6 && available <= 1e-6);
        var dialog = new NoStockDialog(productName, available, reservedElsewhere);
        PosDialogHost.Show(dialog, this);
    }

    private ICartService ResolveCartService() =>
        _cartService ??= App.GetRequiredService<ICartService>();

    private IDeferredCartService ResolveDeferredCartService() =>
        _deferredCartService ??= App.GetRequiredService<IDeferredCartService>();

    private static double ParseManualQuantity(string raw, bool mustWeigh)
    {
        raw = (raw ?? "1").Trim().Replace(',', '.');
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var qty))
            return 0;

        return mustWeigh ? Math.Round(qty, 3) : Math.Round(qty, 0);
    }
}
