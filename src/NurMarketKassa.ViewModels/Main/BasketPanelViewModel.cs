using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;

namespace NurMarketKassa.ViewModels.Main;

/// <summary>Правая зона: корзина, чек, оплата.</summary>
public sealed class BasketPanelViewModel : ViewModelBase
{
    private string _barcodeInput = "";
    private string _manualQuantity = "1";
    private string _activeReceiptTitle = "Основной чек";
    private double _subtotal;
    private double _discount;
    private double _total;
    private string _cartMessage = "";
    private bool _isBusy;

    public BasketPanelViewModel()
    {
        AddByBarcodeCommand = new AsyncRelayCommand(AddByBarcodeAsync, CanAddByBarcode);
        PayCommand = new AsyncRelayCommand(PayAsync, () => Lines.Count > 0 && !IsBusy);
        DeferCartCommand = new RelayCommand(() => { /* модуль «Отложенные чеки» */ }, () => Lines.Count > 0);
        DeleteReceiptCommand = new RelayCommand(ClearReceipt, () => Lines.Count > 0);
        ToggleMoreActionsCommand = new RelayCommand(() => IsMoreActionsVisible = !IsMoreActionsVisible);

        ReceiptTabs.Add(new ReceiptTabVm("Основной чек", isActive: true));
        Lines.CollectionChanged += (_, _) => NotifyLineState();
        RecalculateTotals();
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
        }
    }

    private bool _isMoreActionsVisible;

    public bool IsMoreActionsVisible
    {
        get => _isMoreActionsVisible;
        set => SetProperty(ref _isMoreActionsVisible, value);
    }

    public bool HasLines => Lines.Count > 0;
    public bool IsEmpty => Lines.Count == 0;

    public ICommand AddByBarcodeCommand { get; }
    public ICommand PayCommand { get; }
    public ICommand DeferCartCommand { get; }
    public ICommand DeleteReceiptCommand { get; }
    public ICommand ToggleMoreActionsCommand { get; }

    private bool CanAddByBarcode() =>
        !IsBusy && !string.IsNullOrWhiteSpace(BarcodeInput);

    private async Task AddByBarcodeAsync()
    {
        // Модуль «Корзина» — IBarcodeInputService + ICartService.
        IsBusy = true;
        try
        {
            await Task.Delay(100).ConfigureAwait(true);
            CartMessage = "Добавление товара будет доступно после подключения ICartService.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PayAsync()
    {
        // Модуль «Оплата» — CheckoutViewModel + IWindowService.
        IsBusy = true;
        try
        {
            await Task.Delay(100).ConfigureAwait(true);
            CartMessage = "Оплата будет доступна после подключения CheckoutDialog.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearReceipt()
    {
        Lines.Clear();
        RecalculateTotals();
        CartMessage = "";
        RaiseCartCommands();
    }

    private void RecalculateTotals()
    {
        Subtotal = Lines.Sum(l => l.LineTotal);
        Discount = 0;
        Total = Math.Max(0, Subtotal - Discount);
    }

    private void RaiseCartCommands()
    {
        NotifyLineState();
        (PayCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (DeferCartCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteReceiptCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void NotifyLineState()
    {
        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

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
