using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Models;
using NurMarketKassa.Services;

namespace NurMarketKassa.ViewModels;

public sealed class WarehouseViewModel : INotifyPropertyChanged
{
    public static readonly string[] WriteOffReasons =
    {
        "Брак",
        "Просрочка",
        "Порча упаковки",
        "Другое",
    };

    private readonly IInventoryService _inventoryService;
    private readonly IProductCatalogLookup _catalog;
    private readonly ILocalStockProvider _localStock;
    private readonly IStockCatalogUpdater _catalogUpdater;
    private readonly IUserPrompts _userPrompts;
    private bool _isBusy;
    private string _writeOffProductName = "";
    private string? _writeOffProductId;
    private string _writeOffBarcode = "";
    private double _writeOffQuantity = 1;
    private string _writeOffReason = WriteOffReasons[0];
    private RevisionLineVm? _focusedRevisionLine;

    public WarehouseViewModel(
        IInventoryService inventoryService,
        IProductCatalogLookup catalog,
        ILocalStockProvider localStock,
        IStockCatalogUpdater catalogUpdater,
        IUserPrompts userPrompts)
    {
        _inventoryService = inventoryService;
        _catalog = catalog;
        _localStock = localStock;
        _catalogUpdater = catalogUpdater;
        _userPrompts = userPrompts;

        CommitRevisionCommand = new AsyncRelayCommand(CommitRevisionAsync, () => !IsBusy && RevisionLines.Count > 0);
        WriteOffCommand = new AsyncRelayCommand(WriteOffAsync, CanWriteOff);
    }

    public ObservableCollection<RevisionLineVm> RevisionLines { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string WriteOffProductName
    {
        get => _writeOffProductName;
        set { _writeOffProductName = value; OnPropertyChanged(); }
    }

    public string WriteOffBarcode
    {
        get => _writeOffBarcode;
        set { _writeOffBarcode = value; OnPropertyChanged(); }
    }

    public double WriteOffQuantity
    {
        get => _writeOffQuantity;
        set { _writeOffQuantity = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    public string WriteOffReason
    {
        get => _writeOffReason;
        set { _writeOffReason = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<string> WriteOffReasonOptions => WriteOffReasons;

    public RevisionLineVm? FocusedRevisionLine
    {
        get => _focusedRevisionLine;
        set { _focusedRevisionLine = value; OnPropertyChanged(); }
    }

    public ICommand CommitRevisionCommand { get; }
    public ICommand WriteOffCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<RevisionLineVm>? RevisionLineAdded;

    public async Task EnsureCatalogLoadedAsync()
    {
        if (CatalogCacheService.Products.Count == 0)
            await Task.Run(CatalogCacheService.LoadFromDatabase).ConfigureAwait(true);
    }

    public void HandleBarcodeScan(string barcode, bool isRevisionTab)
    {
        var code = (barcode ?? "").Trim();
        if (code.Length == 0)
            return;

        var product = _catalog.FindByBarcode(code);
        if (product == null)
        {
            _userPrompts.ShowToast($"Товар не найден: {code}", isWarning: true);
            return;
        }

        if (isRevisionTab)
            AddOrIncrementRevisionLine(product);
        else
            SelectWriteOffProduct(product, code);
    }

    private void AddOrIncrementRevisionLine(Core.Domain.CatalogProductInfo product)
    {
        var existing = RevisionLines.FirstOrDefault(
            line => string.Equals(line.ProductId, product.Id, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.ActualQty += 1;
            FocusedRevisionLine = existing;
            RevisionLineAdded?.Invoke(existing);
            return;
        }

        var expected = _localStock.GetExpectedQuantity(product.Id);
        var line = new RevisionLineVm
        {
            ProductId = product.Id,
            Barcode = product.Barcode ?? "",
            ProductName = product.Title,
            ExpectedQty = expected,
            ActualQty = 1,
        };

        RevisionLines.Add(line);
        FocusedRevisionLine = line;
        RevisionLineAdded?.Invoke(line);
        CommandManager.InvalidateRequerySuggested();
    }

    private void SelectWriteOffProduct(Core.Domain.CatalogProductInfo product, string barcode)
    {
        _writeOffProductId = product.Id;
        WriteOffProductName = product.Title;
        WriteOffBarcode = barcode;
        if (WriteOffQuantity <= 0)
            WriteOffQuantity = 1;

        OnPropertyChanged(nameof(WriteOffProductName));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task CommitRevisionAsync()
    {
        var lines = RevisionLines
            .Where(line => Math.Abs(line.ActualQty - line.ExpectedQty) > 1e-9)
            .Select(line => new InventoryLineDto(line.ProductId, line.ExpectedQty, line.ActualQty))
            .ToList();

        if (lines.Count == 0)
        {
            _userPrompts.ShowToast("Нет расхождений для фиксации.", isWarning: true);
            return;
        }

        IsBusy = true;
        try
        {
            var userId = string.IsNullOrWhiteSpace(App.CurrentUserId) ? "cashier" : App.CurrentUserId;
            var ok = await _inventoryService.CommitRevisionAsync(lines, userId).ConfigureAwait(true);
            if (!ok)
            {
                _userPrompts.ShowToast("Не удалось зафиксировать ревизию.", isWarning: true);
                return;
            }

            foreach (var line in RevisionLines)
                _catalogUpdater.UpdateCatalogStock(line.ProductId, line.ActualQty);

            RevisionLines.Clear();
            _userPrompts.ShowToast("Ревизия зафиксирована.");
        }
        catch (Exception ex)
        {
            _userPrompts.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanWriteOff() =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(_writeOffProductId)
        && WriteOffQuantity > 0
        && !string.IsNullOrWhiteSpace(WriteOffReason);

    private async Task WriteOffAsync()
    {
        if (!CanWriteOff())
        {
            _userPrompts.ShowToast("Заполните товар, количество и причину списания.", isWarning: true);
            return;
        }

        IsBusy = true;
        try
        {
            var authorizedBy = string.IsNullOrWhiteSpace(App.CurrentUserId) ? "manager" : App.CurrentUserId;
            var ok = await _inventoryService.WriteOffProductAsync(
                _writeOffProductId!,
                WriteOffQuantity,
                WriteOffReason,
                authorizedBy).ConfigureAwait(true);

            if (!ok)
            {
                _userPrompts.ShowToast("Не удалось списать товар.", isWarning: true);
                return;
            }

            StockSyncService.DecrementLocalStock(_writeOffProductId!, WriteOffQuantity);
            ResetWriteOffForm();
            _userPrompts.ShowToast("Товар списан.");
        }
        catch (Exception ex)
        {
            _userPrompts.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ResetWriteOffForm()
    {
        _writeOffProductId = null;
        WriteOffProductName = "";
        WriteOffBarcode = "";
        WriteOffQuantity = 1;
        WriteOffReason = WriteOffReasons[0];
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
