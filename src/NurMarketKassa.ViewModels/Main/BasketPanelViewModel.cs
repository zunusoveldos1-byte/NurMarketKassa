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
        DeleteReceiptCommand = new RelayCommand(ClearReceipt, () => HasItems);
        ClearCartCommand = DeleteReceiptCommand;
        RemoveLineCommand = new RelayCommand<CartLineItemVm>(RemoveLine, line => line != null && !string.IsNullOrEmpty(line.ItemId));
        ToggleMoreActionsCommand = new RelayCommand(() => IsMoreActionsVisible = !IsMoreActionsVisible);
        OpenDeferredCartsCommand = new AsyncRelayCommand(OpenDeferredCartsAsync, () => !IsBusy);
        ApplyOrderDiscountCommand = new AsyncRelayCommand(ApplyOrderDiscountAsync, () => HasItems && !IsBusy);

        ReceiptTabs.Add(new ReceiptTabVm("Основной чек", isActive: true));
        Lines.CollectionChanged += (_, _) => NotifyLineState();
        EnsureCartInitialized();
        UpdateCartTotals();
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
    public string PayButtonText => $"Оплатить {Total.ToString("0.00", CultureInfo.InvariantCulture)} сом";

    public string CartMessage
    {
        get => _cartMessage;
        set => SetProperty(ref _cartMessage, value ?? "");
    }

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
        }
    }

    private bool _isMoreActionsVisible;

    public bool IsMoreActionsVisible
    {
        get => _isMoreActionsVisible;
        set => SetProperty(ref _isMoreActionsVisible, value);
    }

    public bool HasItems => Lines.Count > 0;
    public bool HasLines => HasItems;
    public bool IsEmpty => Lines.Count == 0;

    public ICommand AddByBarcodeCommand { get; }
    public ICommand PayCommand { get; }
    public ICommand DeferCartCommand { get; }
    public ICommand DeleteReceiptCommand { get; }
    public ICommand ClearCartCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand ToggleMoreActionsCommand { get; }
    public ICommand OpenDeferredCartsCommand { get; }
    public ICommand ApplyOrderDiscountCommand { get; }

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
            CartMessage = "";
        }
        catch (Exception ex)
        {
            PosLogger.Log($"CART add failed: {ex}", "CART");
            _prompts.ShowError("Не удалось добавить товар в чек.");
        }
    }

    public void CreateNewReceipt()
    {
        _cart.ResetForNewReceipt();
        SyncLinesFromCart();
        UpdateCartTotals();
        CartMessage = "Создан новый чек.";
    }

    public void ClearAfterShiftClose()
    {
        _cart.Clear();
        SyncLinesFromCart();
        UpdateCartTotals();
        CartMessage = "";
        PushCustomerDisplay();
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

        await RunOnUiThreadAsync(() =>
        {
            IsBusy = true;
            _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Processing, "Идёт оплата...");
        }).ConfigureAwait(false);

        try
        {
            if (_checkoutUiFlow != null && !await _checkoutUiFlow.PrepareCheckoutAsync().ConfigureAwait(false))
            {
                await RunOnUiThreadAsync(() =>
                    _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Idle)).ConfigureAwait(false);
                return;
            }

            var totals = CartTotalsCalculator.Calculate(_cart.Root);
            var checkoutVm = new CheckoutViewModel(totals, _orderDiscountPercent, _orderDiscountSum);
            var confirmed = await _windowService
                .ShowDialogAsync<CheckoutViewModel, bool?>(checkoutVm)
                .ConfigureAwait(false);

            if (confirmed != true)
            {
                await RunOnUiThreadAsync(() =>
                    _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Idle)).ConfigureAwait(false);
                return;
            }

            var cashReceived = checkoutVm.PaymentMethod == "cash"
                ? CheckoutValidation.NormalizeDecimal(checkoutVm.CashReceived) is { Length: > 0 } normalized
                    ? normalized
                    : totals.TotalDue.ToString("0.00", CultureInfo.InvariantCulture)
                : "0.00";

            var result = await _checkout.CheckoutAsync(new PosCheckoutRequest
            {
                PaymentMethod = checkoutVm.PaymentMethod,
                CashReceived = cashReceived,
                PrintReceipt = checkoutVm.IsPrintReceiptEnabled,
                OrderDiscountBody = checkoutVm.PendingOrderDiscountBody,
            }).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                await RunOnUiThreadAsync(() =>
                {
                    _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Failed, result.ErrorMessage);
                    _prompts.ShowError(result.ErrorMessage ?? PaymentErrorMessages.GenericFailure);
                }).ConfigureAwait(false);
                return;
            }

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
                await _checkoutUiFlow.ShowPaymentSuccessAsync(totals.TotalDue, checkoutVm.IsPrintReceiptEnabled)
                    .ConfigureAwait(false);
            else if (!string.IsNullOrWhiteSpace(result.InfoMessage) && result.SavedOffline)
                await _dialogService.ShowInfoAsync(result.InfoMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"PAY failed: {ex}", "PAYMENT");
            await RunOnUiThreadAsync(() =>
            {
                _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Failed, ex.Message);
                _prompts.ShowError(PaymentErrorMessages.GenericFailure);
            }).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsBusy = false).ConfigureAwait(false);
            _ = Task.Delay(4000).ContinueWith(_ =>
                _customerDisplay.SetPaymentStatus(CustomerDisplayPaymentStatus.Idle));
        }
    }

    private async Task DeferCartAsync()
    {
        await RunOnUiThreadAsync(() => IsBusy = true).ConfigureAwait(false);
        try
        {
            var result = await _deferredCart.DeferCurrentCartAsync().ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                await RunOnUiThreadAsync(() =>
                    _prompts.ShowWarning(result.ErrorMessage ?? "Не удалось отложить чек.")).ConfigureAwait(false);
                return;
            }

            await RunOnUiThreadAsync(() =>
            {
                SyncLinesFromCart();
                UpdateCartTotals();
                CartMessage = $"Отложено: «{result.Label}».";
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

    private void ClearReceipt()
    {
        _cart.ResetForNewReceipt();
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

    private void EnsureCartInitialized()
    {
        if (_cart.HasCart)
            return;

        _cart.ResetForNewReceipt();
    }

    private void SyncLinesFromCart()
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
            });
        }

        RaiseCartCommands();
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
            PushCustomerDisplay();
            return;
        }

        var totals = CartTotalsCalculator.Calculate(_cart.Root);
        Subtotal = totals.Subtotal;
        Discount = totals.LineDiscounts + totals.OrderDiscount;
        Total = totals.TotalDue;
        LineCount = totals.LineCount;
        TotalQuantity = _cart.TotalQuantity;
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
        (DeleteReceiptCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearCartCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task OpenDeferredCartsAsync()
    {
        if (_openDeferredCarts is null)
            return;

        await RunOnUiThreadAsync(() => IsMoreActionsVisible = false).ConfigureAwait(false);
        await _openDeferredCarts().ConfigureAwait(true);
        await RunOnUiThreadAsync(RefreshFromCart).ConfigureAwait(false);
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
}

/// <summary>
/// Этот файл описывает вкладку чека в панели корзины:
/// отображает заголовок вкладки и признак активного чека.
/// </summary>
public sealed class ReceiptTabVm
{
    public ReceiptTabVm(string title, bool isActive = false)
    {
        Title = title;
        IsActive = isActive;
    }

    public string Title { get; }
    public bool IsActive { get; }
}
