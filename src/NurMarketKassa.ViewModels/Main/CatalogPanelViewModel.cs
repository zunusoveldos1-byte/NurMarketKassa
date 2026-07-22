using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Interfaces;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;
using NurMarketKassa.Ui.Shared;

namespace NurMarketKassa.ViewModels.Main;

/// <summary>
/// Этот файл отвечает за отображение каталога на главном экране кассира:
/// загрузка из SQLite, синхронизация с REST API сайта, поиск и добавление товара в чек.
/// </summary>
public sealed class CatalogPanelViewModel : ViewModelBase
{
    private readonly ICatalogCacheService _catalogCache;
    private readonly IDispatcher _dispatcher;
    private readonly IConnectivityService _connectivity;
    private readonly Action<CatalogProductTileVm>? _onProductSelected;

    private int _selectedTabIndex;
    private string _searchText = "";
    private bool _isLoading;
    private string _statusText = "Загрузка каталога…";
    private string _productCountText = "";
    private List<CatalogProductTileVm> _allProducts = [];

    public CatalogPanelViewModel(
        ICatalogCacheService catalogCache,
        IDispatcher dispatcher,
        IConnectivityService connectivity,
        Action<CatalogProductTileVm>? onProductSelected = null)
    {
        _catalogCache = catalogCache;
        _dispatcher = dispatcher;
        _connectivity = connectivity;
        _onProductSelected = onProductSelected;

        ClearSearchCommand = new RelayCommand(ClearSearch, () => !string.IsNullOrWhiteSpace(SearchText));
        RefreshCatalogCommand = new AsyncRelayCommand(RefreshCatalogAsync, () => !IsLoading);
        SelectProductCommand = new RelayCommand<CatalogProductTileVm>(SelectProduct);
        OpenFilterCommand = new RelayCommand(() => { /* модуль «Фильтр» */ });
        OpenWarehouseCommand = new RelayCommand(() => { /* модуль «Склад» */ });

        Products.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasProducts));
            OnPropertyChanged(nameof(IsCatalogEmpty));
        };
    }

    public ObservableCollection<CatalogTabVm> Tabs { get; } =
    [
        new CatalogTabVm("Все товары", "\uE8B7"),
        new CatalogTabVm("Весовые", "\uE9D9"),
        new CatalogTabVm("Штучные", "\uE7B8"),
    ];

    public ObservableCollection<CatalogProductTileVm> Products { get; } = new();

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!SetProperty(ref _selectedTabIndex, value))
                return;
            ApplyFilter();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? ""))
                return;
            (ClearSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
            ApplyFilter();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value))
                return;
            OnPropertyChanged(nameof(IsCatalogEmpty));
            (RefreshCatalogCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value ?? "");
    }

    public string ProductCountText
    {
        get => _productCountText;
        set => SetProperty(ref _productCountText, value ?? "");
    }

    public bool HasProducts => Products.Count > 0;

    public bool IsCatalogEmpty => !HasProducts;

    public ICommand ClearSearchCommand { get; }
    public ICommand RefreshCatalogCommand { get; }
    public ICommand SelectProductCommand { get; }
    public ICommand OpenFilterCommand { get; }
    public ICommand OpenWarehouseCommand { get; }

    private void ClearSearch() => SearchText = "";

    private void SelectProduct(CatalogProductTileVm? product)
    {
        if (product is null)
            return;
        _onProductSelected?.Invoke(product);
    }

    private async Task RefreshCatalogAsync()
    {
        await _dispatcher.InvokeAsync(() => IsLoading = true).ConfigureAwait(false);

        try
        {
            var loaded = _catalogCache.TryLoadFromDatabase();
            if (!loaded || _allProducts.Count == 0)
            {
                var online = await IsOnlineAsync().ConfigureAwait(false);
                if (online)
                {
                    await _dispatcher.InvokeAsync(() =>
                        StatusText = "Синхронизация каталога с сервером…").ConfigureAwait(false);

                    var syncResult = await _catalogCache
                        .SyncCatalogFullAsync()
                        .ConfigureAwait(false);

                    if (!syncResult.Success && !string.IsNullOrWhiteSpace(syncResult.ErrorMessage))
                    {
                        await _dispatcher.InvokeAsync(() =>
                            StatusText = syncResult.ErrorMessage).ConfigureAwait(false);
                    }

                    _catalogCache.TryLoadFromDatabase();
                }
            }

            var products = _catalogCache.GetProducts().ToList();
            await _dispatcher.InvokeAsync(() =>
            {
                _allProducts = products;
                ApplyFilter();
                ProductCountText = $"Товаров: {_allProducts.Count}";
                StatusText = _allProducts.Count > 0
                    ? $"Каталог загружен. Товаров: {_allProducts.Count}."
                    : "Каталог пуст. Проверьте подключение и нажмите «Обновить».";
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"CATALOG refresh failed: {ex}", "CATALOG");
            await _dispatcher.InvokeAsync(() =>
                StatusText = "Ошибка загрузки каталога.").ConfigureAwait(false);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    private async Task<bool> IsOnlineAsync()
    {
        try
        {
            return await _connectivity.IsOnlineAsync().ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private void ApplyFilter()
    {
        Products.Clear();
        var query = _searchText.Trim();

        foreach (var product in _allProducts.Where(MatchesTab).Where(p => MatchesSearch(p, query)))
            Products.Add(product);

        ProductCountText = Products.Count == _allProducts.Count
            ? $"Товаров: {_allProducts.Count}"
            : $"Показано: {Products.Count} из {_allProducts.Count}";
    }

    private bool MatchesTab(CatalogProductTileVm product) => _selectedTabIndex switch
    {
        1 => product.MustWeigh,
        2 => !product.MustWeigh,
        _ => true,
    };

    private static bool MatchesSearch(CatalogProductTileVm product, string query)
    {
        if (query.Length < 2)
            return true;

        var q = query.ToLowerInvariant();
        return product.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
               || (product.Barcode?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
               || product.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
               || (product.Category?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}

/// <summary>
/// Этот файл описывает вкладку фильтра каталога на главном экране кассира
/// (название и иконка: все товары, весовые, штучные и т.д.).
/// </summary>
public sealed class CatalogTabVm
{
    public CatalogTabVm(string title, string iconGlyph)
    {
        Title = title;
        IconGlyph = iconGlyph;
    }

    public string Title { get; }
    public string IconGlyph { get; }
}
