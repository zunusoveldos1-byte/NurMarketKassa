using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Interfaces;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Hardware;
using NurMarketKassa.Ui.Shared;
using NurMarketKassa.ViewModels;

namespace NurMarketKassa.ViewModels.Main;

/// <summary>
/// Этот файл отвечает за панель чека на главном экране кассира:
/// строки корзины, пересчёт итогов, добавление по штрихкоду и управление текущим чеком.
/// </summary>
public sealed class BasketPanelViewModel : ViewModelBase
{
    public const int MaxOpenReceipts = 10;

    private readonly ICartService _cart;
    private readonly IUserPrompts _prompts;
    private readonly IPosCheckoutService _checkout;
    private readonly IDeferredCartService _deferredCart;
    private readonly ICustomerDisplayService _customerDisplay;
    private readonly IWindowService _windowService;
    private readonly IDialogService _dialogService;
    private readonly IDispatcher _dispatcher;
    private readonly IPosCheckoutUiFlow? _checkoutUiFlow;
    private readonly Func<string, CatalogProductTileVm?>? _catalogLookup;
    private readonly Func<CatalogProductTileVm, Task>? _addProductFromCatalog;
    private readonly Func<Task>? _openDeferredCarts;
    private readonly Func<Task>? _applyOrderDiscount;

    private string _barcodeInput = "";
    private string _manualQuantity = "1";
    private string _activeReceiptTitle = "Основной чек";
    private double _subtotal;
    private double _discount;
    private double _total;
    private int _lineCount;
    private double _totalQuantity;
    private string _cartMessage = "";
    private bool _isBusy;
    private string _orderDiscountPercent = "";
    private string _orderDiscountSum = "";
    private int _nextReceiptNumber = 2;
    private string _activeSessionId = "";
    private readonly List<OpenReceiptSession> _sessions = new();
    private bool _suppressTabRebuild;
    private bool _isMoreActionsVisible;

    public BasketPanelViewModel(
        ICartService cart,
        IUserPrompts prompts,
        IPosCheckoutService checkout,
        IDeferredCartService deferredCart,
        ICustomerDisplayService customerDisplay,
        IWindowService windowService,
        IDialogService dialogService,
        IDispatcher dispatcher,
        Func<string, CatalogProductTileVm?>? catalogLookup = null,
        IPosCheckoutUiFlow? checkoutUiFlow = null,
        Func<CatalogProductTileVm, Task>? addProductFromCatalog = null,
        Func<Task>? openDeferredCarts = null,
        Func<Task>? applyOrderDiscount = null)
    {
        _cart = cart;
        _prompts = prompts;
        _checkout = checkout;
        _deferredCart = deferredCart;
        _customerDisplay = customerDisplay;
        _windowService = windowService;
        _dialogService = dialogService;
        _dispatcher = dispatcher;
        _catalogLookup = catalogLookup;
        _checkoutUiFlow = checkoutUiFlow;
        _addProductFromCatalog = addProductFromCatalog;
        _openDeferredCarts = openDeferredCarts;
        _applyOrderDiscount = applyOrderDiscount;

        AddByBarcodeCommand = new AsyncRelayCommand(AddByBarcodeAsync, CanAddByBarcode);
        PayCommand = new AsyncRelayCommand(PayAsync, () => HasItems && !IsBusy);
        DeferCartCommand = new AsyncRelayCommand(DeferCartAsync, () => HasItems && !IsBusy);
        HoldReceiptCommand = DeferCartCommand;
        DeleteReceiptCommand = new RelayCommand(DeleteActiveReceipt, CanDeleteActiveReceiptCore);
        ClearCartCommand = DeleteReceiptCommand;
        NewReceiptCommand = new RelayCommand(CreateNewReceipt);
        SelectReceiptTabCommand = new RelayCommand<ReceiptTabVm>(SelectReceiptTab, tab => tab != null && !tab.IsActive);
        RemoveLineCommand = new RelayCommand<CartLineItemVm>(RemoveLine, line => line != null && !string.IsNullOrEmpty(line.ItemId));
        IncreaseQuantityCommand = new RelayCommand<CartLineItemVm>(IncreaseQuantity, CanChangeLineQuantity);
        DecreaseQuantityCommand = new RelayCommand<CartLineItemVm>(DecreaseQuantity, CanDecreaseLineQuantity);
        WeighLineCommand = new RelayCommand<CartLineItemVm>(WeighLine, line => line is { IsWeight: true } && !string.IsNullOrEmpty(line.ItemId));
        LineDiscountCommand = new RelayCommand<CartLineItemVm>(_ => { }, line => line != null && HasItems);
        IncreaseManualQuantityCommand = new RelayCommand(IncreaseManualQuantity);
        DecreaseManualQuantityCommand = new RelayCommand(DecreaseManualQuantity);
        ToggleMoreActionsCommand = new RelayCommand(() => IsMoreActionsVisible = !IsMoreActionsVisible);
        OpenDeferredCartsCommand = new AsyncRelayCommand(OpenDeferredCartsAsync, () => !IsBusy);
        ApplyOrderDiscountCommand = new AsyncRelayCommand(ApplyOrderDiscountAsync, () => HasItems && !IsBusy);
        ReturnPreviousReceiptCommand = new AsyncRelayCommand(RestoreLastHeldReceiptAsync, () => !IsBusy && HasHeldReceipts);
        RestoreLastHeldReceiptCommand = ReturnPreviousReceiptCommand;

        EnsureCartInitialized();
        EnsurePrimarySession();
        Lines.CollectionChanged += (_, _) => NotifyLineState();
        UpdateCartTotals();
        RebuildReceiptTabs();
        _customerDisplay.Show();
    }

    public ObservableCollection<CartLineItemVm> Lines { get; } = new();
    public ObservableCollection<ReceiptTabVm> ReceiptTabs { get; } = new();

    public string BarcodeInput
    {
        get => _barcodeInput;
        set
        {
            if (!SetProperty(ref _barcodeInput, value ?? ""))
                return;
            (AddByBarcodeCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string ManualQuantity
    {
        get => _manualQuantity;
        set => SetProperty(ref _manualQuantity, value ?? "1");
    }

    public string ActiveReceiptTitle
    {
        get => _activeReceiptTitle;
        set => SetProperty(ref _activeReceiptTitle, value ?? "");
    }

    public double Subtotal
    {
        get => _subtotal;
        private set
        {
            if (!SetProperty(ref _subtotal, value))
                return;
            OnPropertyChanged(nameof(SubtotalDisplay));
        }
    }

    public double Discount
    {
        get => _discount;
        private set
        {
            if (!SetProperty(ref _discount, value))
                return;
            OnPropertyChanged(nameof(DiscountDisplay));
        }
    }

    public double Total
    {
        get => _total;
        private set
        {
            if (!SetProperty(ref _total, value))
                return;
            OnPropertyChanged(nameof(TotalDisplay));
            OnPropertyChanged(nameof(TotalAmount));
            OnPropertyChanged(nameof(PayButtonText));
        }
    }

    public int LineCount
    {
        get => _lineCount;
        private set => SetProperty(ref _lineCount, value);
    }

    public double TotalQuantity
    {
        get => _totalQuantity;
        private set => SetProperty(ref _totalQuantity, value);
    }

    public string SubtotalDisplay => $"{Subtotal.ToString("0.00", CultureInfo.InvariantCulture)} сом";
    public string DiscountDisplay => $"{Discount.ToString("0.00", CultureInfo.InvariantCulture)} сом";
    public string TotalDisplay => $"{Total.ToString("0.00", CultureInfo.InvariantCulture)} сом";
    public string TotalAmount => Total.ToString("0.00", CultureInfo.InvariantCulture);
    public string PayButtonText => "Оплатить";

    public string CartMessage
    {
        get => _cartMessage;
        set
        {
            if (!SetProperty(ref _cartMessage, value ?? ""))
                return;
            OnPropertyChanged(nameof(HasCartMessage));
        }
    }

    public bool HasCartMessage => !string.IsNullOrWhiteSpace(CartMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;
            (AddByBarcodeCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PayCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (DeferCartCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (OpenDeferredCartsCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ApplyOrderDiscountCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ReturnPreviousReceiptCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (DeleteReceiptCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool IsMoreActionsVisible
    {
        get => _isMoreActionsVisible;
        set => SetProperty(ref _isMoreActionsVisible, value);
    }

    public bool HasItems => Lines.Count > 0;
    public bool HasLines => HasItems;
    public bool IsEmpty => Lines.Count == 0;
    public bool HasHeldReceipts => DeferredCartsStore.Count() > 0;
    public bool CanDeleteActiveReceipt => CanDeleteActiveReceiptCore();
    public bool IsAtReceiptLimit => _sessions.Count >= MaxOpenReceipts;

    public ICommand AddByBarcodeCommand { get; }
    public ICommand PayCommand { get; }
    public ICommand DeferCartCommand { get; }
    public ICommand HoldReceiptCommand { get; }
    public ICommand DeleteReceiptCommand { get; }
    public ICommand ClearCartCommand { get; }
    public ICommand NewReceiptCommand { get; }
    public ICommand SelectReceiptTabCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand IncreaseQuantityCommand { get; }
    public ICommand DecreaseQuantityCommand { get; }
    public ICommand WeighLineCommand { get; }
    public ICommand LineDiscountCommand { get; }
    public ICommand IncreaseManualQuantityCommand { get; }
    public ICommand DecreaseManualQuantityCommand { get; }
    public ICommand ToggleMoreActionsCommand { get; }
    public ICommand OpenDeferredCartsCommand { get; }
    public ICommand ApplyOrderDiscountCommand { get; }
    public ICommand ReturnPreviousReceiptCommand { get; }
    public ICommand RestoreLastHeldReceiptCommand { get; }

    public void RefreshFromCart()
    {
        SyncLinesFromCart();
        UpdateCartTotals();
    }

    public void AddProductFromCatalog(CatalogProductTileVm product)
    {
        if (product is null)
            return;

        try
        {
            EnsureCartInitialized();
            var qty = ParseQuantity(ManualQuantity, product.MustWeigh);
            _cart.AddItem(product, qty);
            SyncLinesFromCart();
            UpdateCartTotals();
            CartMessage = "Товар добавлен.";
        }
        catch (Exception ex)
        {
            PosLogger.Log($"CART add failed: {ex}", "CART");
            _prompts.ShowError("Не удалось добавить товар в чек.");
        }
    }

    public void CreateNewReceipt()
    {
        if (!TryEnsureReceiptSlotAvailable())
            return;

        PersistActiveSessionSnapshot();

        var number = _nextReceiptNumber++;
        var session = new OpenReceiptSession
        {
            Id = Guid.NewGuid().ToString("N"),
            Number = number,
            BaseName = $"Чек {number}",
            CartJson = "{}",
        };
        _sessions.Add(session);
        _activeSessionId = session.Id;
        ActiveReceiptTitle = session.BaseName;

        _cart.ResetForNewReceipt();
        SyncLinesFromCart();
        UpdateCartTotals();
        CartMessage = $"Открыт «{session.BaseName}».";
        RaiseCartCommands();
    }

    public void ClearAfterShiftClose()
    {
        _cart.Clear();
        _sessions.Clear();
        _nextReceiptNumber = 2;
        EnsurePrimarySession();
        SyncLinesFromCart();
        UpdateCartTotals();
        CartMessage = "";
        PushCustomerDisplay();
        RaiseCartCommands();
    }

    private void SelectReceiptTab(ReceiptTabVm? tab)
    {
        if (tab is null || tab.IsActive || string.IsNullOrWhiteSpace(tab.Id))
            return;

        var target = _sessions.FirstOrDefault(s => s.Id == tab.Id);
        if (target is null)
            return;

        PersistActiveSessionSnapshot();
        _activeSessionId = target.Id;
        ActiveReceiptTitle = target.BaseName;
        ApplySessionToCart(target);
        SyncLinesFromCart();
        UpdateCartTotals();
        CartMessage = $"Активен «{target.BaseName}».";
        RaiseCartCommands();
    }

    private bool CanAddByBarcode() =>
        !IsBusy && !string.IsNullOrWhiteSpace(BarcodeInput);

    private async Task AddByBarcodeAsync()
    {
        await RunOnUiThreadAsync(() => IsBusy = true).ConfigureAwait(false);
        try
        {
            var barcode = BarcodeInput.Trim();
            var product = _catalogLookup?.Invoke(barcode);

            if (product is null)
            {
                await RunOnUiThreadAsync(() =>
                    _prompts.ShowWarning("Товар не найден в каталоге.")).ConfigureAwait(false);
                return;
            }

            if (_addProductFromCatalog != null)
            {
                await _addProductFromCatalog(product).ConfigureAwait(true);
                await RunOnUiThreadAsync(() => BarcodeInput = "").ConfigureAwait(false);
                return;
            }

            await RunOnUiThreadAsync(() => AddProductFromCatalog(product)).ConfigureAwait(false);
            await RunOnUiThreadAsync(() => BarcodeInput = "").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"CART barcode add failed: {ex}", "CART");
            await RunOnUiThreadAsync(() =>
                _prompts.ShowError("Ошибка добавления товара.")).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    private async Task PayAsync()
    {
        if (Lines.Count == 0)
            return;

        PosLogger.Log("PAY start", "PAYMENT");

        await RunOnUiThreadAsync(() =>
        {
            IsBusy = true;
            _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Processing, "Идёт оплата...");
        }).ConfigureAwait(false);

        try
        {
            if (_checkoutUiFlow != null)
            {
                PosLogger.Log("PAY prepare checkout (stock check)", "PAYMENT");
                var prepared = await _checkoutUiFlow.PrepareCheckoutAsync().ConfigureAwait(false);
                if (!prepared)
                {
                    PosLogger.Log("PAY aborted: stock blocked", "PAYMENT");
                    await RunOnUiThreadAsync(() =>
                        _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Idle)).ConfigureAwait(false);
                    return;
                }
            }

            var totals = CartTotalsCalculator.Calculate(_cart.Root);
            PosLogger.Log(
                $"PAY dialog open: lines={_cart.LineCount}, total={totals.TotalDue:0.00}",
                "PAYMENT");

            // Диалог оплаты всегда открывается через AvaloniaWindowService на UI-потоке.
            var checkoutVm = new CheckoutViewModel(totals, _orderDiscountPercent, _orderDiscountSum);
            var confirmed = await _windowService
                .ShowDialogAsync<CheckoutViewModel, bool?>(checkoutVm)
                .ConfigureAwait(false);

            if (confirmed != true)
            {
                PosLogger.Log("PAY canceled by cashier", "PAYMENT");
                await RunOnUiThreadAsync(() =>
                    _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Idle)).ConfigureAwait(false);
                return;
            }

            var cashReceived = checkoutVm.PaymentMethod == "cash"
                ? CheckoutValidation.NormalizeDecimal(checkoutVm.CashReceived) is { Length: > 0 } normalized
                    ? normalized
                    : totals.TotalDue.ToString("0.00", CultureInfo.InvariantCulture)
                : "0.00";

            PosLogger.Log(
                $"PAY API checkout: method={checkoutVm.PaymentMethod}, cash={cashReceived}, print={checkoutVm.IsPrintReceiptEnabled}",
                "PAYMENT");

            var result = await _checkout.CheckoutAsync(new PosCheckoutRequest
            {
                PaymentMethod = checkoutVm.PaymentMethod,
                CashReceived = cashReceived,
                PrintReceipt = checkoutVm.IsPrintReceiptEnabled,
                OrderDiscountBody = checkoutVm.PendingOrderDiscountBody,
            }).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                var error = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? PaymentErrorMessages.GenericFailure
                    : result.ErrorMessage;
                PosLogger.Log($"PAY failed result: {error}", "PAYMENT");
                await RunOnUiThreadAsync(() =>
                {
                    _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Failed, error);
                    _prompts.ShowError(error);
                }).ConfigureAwait(false);
                return;
            }

            PosLogger.Log(
                $"PAY success: offline={result.SavedOffline}, total={result.TotalAmount:0.00}",
                "PAYMENT");

            await RunOnUiThreadAsync(() =>
            {
                _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Success, "Спасибо за покупку!");
                SyncLinesFromCart();
                UpdateCartTotals();
                CartMessage = result.SavedOffline
                    ? result.InfoMessage ?? "Оплата сохранена локально."
                    : "Оплата выполнена. Новый чек открыт.";
            }).ConfigureAwait(false);

            if (_checkoutUiFlow != null)
            {
                PosLogger.Log("PAY show success dialog", "PAYMENT");
                await _checkoutUiFlow.ShowPaymentSuccessAsync(totals.TotalDue, checkoutVm.IsPrintReceiptEnabled)
                    .ConfigureAwait(false);
            }
            else if (!string.IsNullOrWhiteSpace(result.InfoMessage) && result.SavedOffline)
            {
                await _dialogService.ShowInfoAsync(result.InfoMessage).ConfigureAwait(false);
            }

            PosLogger.Log("PAY finished", "PAYMENT");
        }
        catch (Exception ex)
        {
            PosLogger.Log($"PAY failed: {ex}", "PAYMENT");
            System.Diagnostics.Debug.WriteLine(ex.ToString());
            var error = PaymentErrorMessages.ForCashier(ex);
            await RunOnUiThreadAsync(() =>
            {
                _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Failed, error);
                _prompts.ShowError(error);
            }).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsBusy = false).ConfigureAwait(false);
            _ = ResetCustomerDisplayStatusAfterDelayAsync();
        }
    }

    private async Task ResetCustomerDisplayStatusAfterDelayAsync()
    {
        try
        {
            await Task.Delay(4000).ConfigureAwait(false);
            await RunOnUiThreadAsync(() =>
                _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Idle)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"PAY status reset failed: {ex}", "PAYMENT");
        }
    }

    private async Task DeferCartAsync()
    {
        await RunOnUiThreadAsync(() => IsBusy = true).ConfigureAwait(false);
        try
        {
            if (!HasItems)
            {
                await RunOnUiThreadAsync(() =>
                    _prompts.ShowWarning("Корзина пуста — нечего откладывать.")).ConfigureAwait(false);
                return;
            }

            if (_sessions.Count >= MaxOpenReceipts)
            {
                await RunOnUiThreadAsync(ShowReceiptLimitReached).ConfigureAwait(false);
                return;
            }

            PersistActiveSessionSnapshot();
            var snapshotJson = OpenReceiptSnapshot.CloneCartJson(OpenReceiptSnapshot.Capture(_cart).CartJson);

            var result = await _deferredCart
                .DeferCurrentCartAsync(startNewSale: false)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                await RunOnUiThreadAsync(() =>
                    _prompts.ShowWarning(result.ErrorMessage ?? "Не удалось отложить чек.")).ConfigureAwait(false);
                return;
            }

            await RunOnUiThreadAsync(() =>
            {
                // Копия товаров уходит во вкладку «Чек N», фокус остаётся на текущем (очищенном) чеке.
                var number = _nextReceiptNumber++;
                var copySession = new OpenReceiptSession
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Number = number,
                    BaseName = $"Чек {number}",
                    CartJson = snapshotJson,
                };
                _sessions.Add(copySession);

                _cart.ResetForNewReceipt();
                PersistActiveSessionSnapshot();
                SyncLinesFromCart();
                UpdateCartTotals();
                CartMessage =
                    $"Отложено: «{result.Label}». Копия во вкладке «{copySession.BaseName}». Текущий чек очищен.";
                NotifyHeldReceiptsChanged();
                RaiseCartCommands();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"DEFER failed: {ex}", "DEFER");
            await RunOnUiThreadAsync(() =>
                _prompts.ShowError("Не удалось отложить чек.")).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    private async Task RestoreLastHeldReceiptAsync()
    {
        await RunOnUiThreadAsync(() => IsBusy = true).ConfigureAwait(false);
        try
        {
            var latest = DeferredCartsStore.TryGetLatest();
            if (latest is null)
            {
                await RunOnUiThreadAsync(() =>
                    _prompts.ShowWarning("Нет отложенных чеков для возврата.")).ConfigureAwait(false);
                return;
            }

            await RunOnUiThreadAsync(() =>
            {
                PersistActiveSessionSnapshot();
                var currentHasItems = HasItems;
                var currentJson = OpenReceiptSnapshot.CloneCartJson(OpenReceiptSnapshot.Capture(_cart).CartJson);

                if (currentHasItems)
                {
                    // Вариант Б: SWAP — текущий уходит в архив.
                    DeferredCartsStore.Add(new DeferredCartEntry
                    {
                        Label = BuildHeldLabel("Обмен"),
                        CartJson = currentJson,
                    });
                }

                ApplyCartJson(latest.CartJson);
                DeferredCartsStore.RemoveIds(new[] { latest.Id });

                SyncLinesFromCart();
                UpdateCartTotals();
                CartMessage = currentHasItems
                    ? "Чеки обменяны с последним отложенным."
                    : $"Возвращён отложенный чек «{latest.Label}».";
                NotifyHeldReceiptsChanged();
                RaiseCartCommands();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"RESTORE held failed: {ex}", "DEFER");
            await RunOnUiThreadAsync(() =>
                _prompts.ShowError("Не удалось вернуть отложенный чек.")).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    private bool CanDeleteActiveReceiptCore()
    {
        var active = GetActiveSession();
        return active is { IsPrimary: false } && !IsBusy;
    }

    private void DeleteActiveReceipt()
    {
        var active = GetActiveSession();
        if (active is null || active.IsPrimary)
        {
            _prompts.ShowWarning("«Основной чек» удалить нельзя.");
            return;
        }

        var index = _sessions.FindIndex(s => s.Id == active.Id);
        if (index < 0)
            return;

        _sessions.RemoveAt(index);
        var prevIndex = Math.Clamp(index - 1, 0, _sessions.Count - 1);
        var previous = _sessions[prevIndex];
        _activeSessionId = previous.Id;
        ActiveReceiptTitle = previous.BaseName;
        ApplySessionToCart(previous);
        SyncLinesFromCart();
        UpdateCartTotals();
        CartMessage = $"Чек удалён. Активен «{previous.BaseName}».";
        RaiseCartCommands();
    }

    private void ClearReceipt()
    {
        _cart.ResetForNewReceipt();
        PersistActiveSessionSnapshot();
        SyncLinesFromCart();
        UpdateCartTotals();
        CartMessage = "";
        RaiseCartCommands();
    }

    private void RemoveLine(CartLineItemVm? line)
    {
        if (line is null || string.IsNullOrWhiteSpace(line.ItemId))
            return;

        _cart.RemoveItem(line.ItemId);
        SyncLinesFromCart();
        UpdateCartTotals();
        RaiseCartCommands();
    }

    private static bool CanChangeLineQuantity(CartLineItemVm? line) =>
        line != null && !string.IsNullOrEmpty(line.ItemId);

    private static bool CanDecreaseLineQuantity(CartLineItemVm? line) =>
        CanChangeLineQuantity(line) && line!.Quantity > 1;

    private void IncreaseQuantity(CartLineItemVm? line)
    {
        if (!CanChangeLineQuantity(line))
            return;

        var step = line!.IsWeight ? 0.1 : 1;
        var next = Math.Round(line.Quantity + step, line.IsWeight ? 3 : 0);
        _cart.UpdateQuantity(line.ItemId, next);
        SyncLinesFromCart();
        UpdateCartTotals();
        RaiseCartCommands();
    }

    private void DecreaseQuantity(CartLineItemVm? line)
    {
        if (!CanChangeLineQuantity(line))
            return;

        if (line!.Quantity <= 1)
        {
            CartMessage = "Меньше 1 нельзя.";
            return;
        }

        var step = line.IsWeight ? 0.1 : 1;
        var next = Math.Round(line.Quantity - step, line.IsWeight ? 3 : 0);
        if (next < 1)
            next = 1;

        _cart.UpdateQuantity(line.ItemId, next);
        SyncLinesFromCart();
        UpdateCartTotals();
        RaiseCartCommands();
    }

    private void WeighLine(CartLineItemVm? line)
    {
        if (line is null || !line.IsWeight || string.IsNullOrWhiteSpace(line.ItemId))
            return;

        CartMessage = "Укажите вес через диалог взвешивания в каталоге или отсканируйте товар заново.";
    }

    private void IncreaseManualQuantity()
    {
        if (!double.TryParse(ManualQuantity.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var qty)
            || qty < 1)
            qty = 1;
        ManualQuantity = Math.Round(qty + 1, 0).ToString("0", CultureInfo.InvariantCulture);
    }

    private void DecreaseManualQuantity()
    {
        if (!double.TryParse(ManualQuantity.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var qty)
            || qty <= 1)
        {
            ManualQuantity = "1";
            return;
        }

        ManualQuantity = Math.Round(qty - 1, 0).ToString("0", CultureInfo.InvariantCulture);
    }

    private void RefreshReceiptTabs()
    {
        if (_suppressTabRebuild)
            return;

        PersistActiveSessionSnapshot();
        RebuildReceiptTabs();
    }

    private void RebuildReceiptTabs()
    {
        ReceiptTabs.Clear();
        foreach (var session in _sessions)
        {
            var isActive = session.Id == _activeSessionId;
            var (lines, total) = isActive
                ? (LineCount, Total)
                : GetSessionSummary(session.CartJson);
            ReceiptTabs.Add(new ReceiptTabVm(
                session.Id,
                FormatTabTitle(session.BaseName, lines, total),
                isActive,
                SelectReceiptTabCommand));
        }

        (SelectReceiptTabCommand as RelayCommand<ReceiptTabVm>)?.RaiseCanExecuteChanged();
    }

    private void EnsurePrimarySession(bool resetCart = false)
    {
        if (_sessions.Count == 0)
        {
            var primary = new OpenReceiptSession
            {
                Id = Guid.NewGuid().ToString("N"),
                Number = 1,
                BaseName = "Основной чек",
                CartJson = "{}",
            };
            _sessions.Add(primary);
            _activeSessionId = primary.Id;
            ActiveReceiptTitle = primary.BaseName;
            _nextReceiptNumber = Math.Max(_nextReceiptNumber, 2);
        }

        if (resetCart)
        {
            _cart.ResetForNewReceipt();
            var active = GetActiveSession();
            if (active != null)
                active.CartJson = "{}";
        }
    }

    private OpenReceiptSession? GetActiveSession() =>
        _sessions.FirstOrDefault(s => s.Id == _activeSessionId) ?? _sessions.FirstOrDefault();

    private void PersistActiveSessionSnapshot()
    {
        var active = GetActiveSession();
        if (active is null)
            return;

        active.CartJson = OpenReceiptSnapshot.Capture(_cart).CartJson;
        active.BaseName = string.IsNullOrWhiteSpace(ActiveReceiptTitle)
            ? active.BaseName
            : ActiveReceiptTitle.Trim();
    }

    private void ApplySessionToCart(OpenReceiptSession session) =>
        ApplyCartJson(session.CartJson);

    private void ApplyCartJson(string? cartJson)
    {
        if (string.IsNullOrWhiteSpace(cartJson) || cartJson == "{}")
        {
            _cart.ResetForNewReceipt();
            return;
        }

        new OpenReceiptSnapshot { CartJson = cartJson }.ApplyTo(_cart);
        if (!_cart.HasCart)
            _cart.ResetForNewReceipt();
    }

    private bool TryEnsureReceiptSlotAvailable()
    {
        if (_sessions.Count < MaxOpenReceipts)
            return true;

        ShowReceiptLimitReached();
        return false;
    }

    private void ShowReceiptLimitReached()
    {
        const string message =
            "Достигнут лимит открытых чеков (максимум 10). Завершите или удалите существующие чеки.";
        _prompts.ShowWarning(message);
        _ = _dialogService.ShowInfoAsync(message);
    }

    private static string BuildHeldLabel(string prefix) =>
        $"{prefix} #{DeferredCartsStore.Count() + 1} ({DateTime.Now:HH:mm})";

    private void NotifyHeldReceiptsChanged()
    {
        OnPropertyChanged(nameof(HasHeldReceipts));
        (ReturnPreviousReceiptCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private static (int Lines, double Total) GetSessionSummary(string cartJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                string.IsNullOrWhiteSpace(cartJson) ? "{}" : cartJson);
            var root = doc.RootElement;
            var lines = OpenReceiptSnapshot.CountLines(cartJson);
            var total = root.ValueKind == System.Text.Json.JsonValueKind.Object
                ? CartTotalsCalculator.Calculate(root).TotalDue
                : 0;
            return (lines, total);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static string FormatTabTitle(string baseName, int lineCount, double total) =>
        $"{baseName} • {lineCount} тов. • {total.ToString("0.00", CultureInfo.InvariantCulture)} сом";

    private void EnsureCartInitialized()
    {
        if (_cart.HasCart)
            return;

        _cart.ResetForNewReceipt();
    }

    private void SyncLinesFromCart()
    {
        _suppressTabRebuild = true;
        try
        {
            Lines.Clear();
            foreach (var item in _cart.Items)
            {
                Lines.Add(new CartLineItemVm
                {
                    ItemId = item.Id ?? "",
                    Title = item.Name,
                    Unit = item.MustWeigh ? "кг" : "шт",
                    UnitPrice = (double)item.UnitPrice,
                    Quantity = item.Quantity,
                    LineTotal = (double)item.LineTotal,
                    IsWeight = item.MustWeigh,
                    RemoveCommand = RemoveLineCommand,
                    IncreaseCommand = IncreaseQuantityCommand,
                    DecreaseCommand = DecreaseQuantityCommand,
                    WeighCommand = WeighLineCommand,
                    DiscountCommand = LineDiscountCommand,
                });
            }
        }
        finally
        {
            _suppressTabRebuild = false;
        }

        RaiseCartCommands();
        PersistActiveSessionSnapshot();
        RebuildReceiptTabs();
        PushCustomerDisplay();
    }

    private void UpdateCartTotals()
    {
        if (!_cart.HasCart)
        {
            Subtotal = 0;
            Discount = 0;
            Total = 0;
            LineCount = 0;
            TotalQuantity = 0;
            PersistActiveSessionSnapshot();
            RebuildReceiptTabs();
            PushCustomerDisplay();
            return;
        }

        var totals = CartTotalsCalculator.Calculate(_cart.Root);
        Subtotal = totals.Subtotal;
        Discount = totals.LineDiscounts + totals.OrderDiscount;
        Total = totals.TotalDue;
        LineCount = totals.LineCount;
        TotalQuantity = _cart.TotalQuantity;
        PersistActiveSessionSnapshot();
        RebuildReceiptTabs();
        PushCustomerDisplay();
    }

    private void PushCustomerDisplay()
    {
        _customerDisplay.UpdateCart(new CustomerDisplayCartSnapshot
        {
            Lines = Lines.Select(line => new CustomerDisplayLine
            {
                Title = line.Title,
                Quantity = line.Quantity,
                Unit = line.Unit,
                LineTotal = line.LineTotal,
            }).ToList(),
            Subtotal = Subtotal,
            Discount = Discount,
            Total = Total,
        });
    }

    private static double ParseQuantity(string? raw, bool mustWeigh)
    {
        if (!double.TryParse(raw?.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var qty)
            || qty <= 0)
            qty = 1;

        return mustWeigh ? Math.Round(qty, 3) : Math.Round(qty, 0);
    }

    private void RaiseCartCommands()
    {
        NotifyLineState();
        (PayCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (DeferCartCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (OpenDeferredCartsCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ApplyOrderDiscountCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ReturnPreviousReceiptCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (DeleteReceiptCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearCartCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (IncreaseQuantityCommand as RelayCommand<CartLineItemVm>)?.RaiseCanExecuteChanged();
        (DecreaseQuantityCommand as RelayCommand<CartLineItemVm>)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanDeleteActiveReceipt));
        OnPropertyChanged(nameof(IsAtReceiptLimit));
        OnPropertyChanged(nameof(HasHeldReceipts));
    }

    private async Task OpenDeferredCartsAsync()
    {
        if (_openDeferredCarts is null)
            return;

        await RunOnUiThreadAsync(() => IsMoreActionsVisible = false).ConfigureAwait(false);
        await _openDeferredCarts().ConfigureAwait(true);
        await RunOnUiThreadAsync(() =>
        {
            RefreshFromCart();
            NotifyHeldReceiptsChanged();
            RaiseCartCommands();
        }).ConfigureAwait(false);
    }

    private async Task ApplyOrderDiscountAsync()
    {
        if (_applyOrderDiscount is null)
            return;

        await RunOnUiThreadAsync(() => IsMoreActionsVisible = false).ConfigureAwait(false);
        await _applyOrderDiscount().ConfigureAwait(true);
    }

    private Task RunOnUiThreadAsync(Action action) =>
        _dispatcher.InvokeAsync(action);

    private void NotifyLineState()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Локальная сессия открытого чека (снимок корзины + имя вкладки).</summary>
    private sealed class OpenReceiptSession
    {
        public string Id { get; init; } = "";
        public int Number { get; init; }
        public string BaseName { get; set; } = "Основной чек";
        public string CartJson { get; set; } = "{}";
        public bool IsPrimary => Number == 1;
    }
}

/// <summary>
/// Вкладка открытого чека в панели корзины.
/// </summary>
public sealed class ReceiptTabVm : ViewModelBase
{
    private string _title;
    private bool _isActive;

    public ReceiptTabVm(string id, string title, bool isActive = false, ICommand? selectCommand = null)
    {
        Id = id;
        _title = title;
        _isActive = isActive;
        SelectCommand = selectCommand;
    }

    public string Id { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? "");
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public ICommand? SelectCommand { get; }
}
