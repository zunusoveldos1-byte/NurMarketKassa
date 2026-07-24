using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Input;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Interfaces;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Api;
using NurMarketKassa.Ui.Shared;

namespace NurMarketKassa.ViewModels.Main;

/// <summary>
/// Каталог кассира: Offline-First (SQLite → UI), затем фоновая синхронизация с API.
/// </summary>
public sealed class CatalogPanelViewModel : ViewModelBase
{
    private readonly ICatalogCacheService _catalogCache;
    private readonly IDispatcher _dispatcher;
    private readonly IConnectivityService _connectivity;
    private readonly Action<CatalogProductTileVm>? _onProductSelected;
    private readonly ICatalogApiService? _catalogApi;
    private readonly MySqlAuditService? _auditDb;
    private readonly IUserPrompts? _prompts;

    private int _selectedTabIndex;
    private string _searchText = "";
    private bool _isLoading;
    private bool _isOfflineBannerVisible;
    private string _statusText = "Загрузка каталога…";
    private string _productCountText = "";
    private List<CatalogProductTileVm> _allProducts = [];

    public CatalogPanelViewModel(
        ICatalogCacheService catalogCache,
        IDispatcher dispatcher,
        IConnectivityService connectivity,
        Action<CatalogProductTileVm>? onProductSelected = null,
        ICatalogApiService? catalogApi = null,
        MySqlAuditService? auditDb = null,
        IUserPrompts? prompts = null)
    {
        _catalogCache = catalogCache;
        _dispatcher = dispatcher;
        _connectivity = connectivity;
        _onProductSelected = onProductSelected;
        _catalogApi = catalogApi;
        _auditDb = auditDb;
        _prompts = prompts;

        ClearSearchCommand = new RelayCommand(ClearSearch, () => !string.IsNullOrWhiteSpace(SearchText));
        RefreshCatalogCommand = new AsyncRelayCommand(RefreshCatalogAsync, () => !IsLoading);
        SelectProductCommand = new RelayCommand<CatalogProductTileVm>(SelectProduct);
        ToggleFavoriteCommand = new RelayCommand<CatalogProductTileVm>(vm => _ = ToggleFavoriteAsync(vm));
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

    /// <summary>Плашка «работа из локальной БД / автономный режим».</summary>
    public bool IsOfflineBannerVisible
    {
        get => _isOfflineBannerVisible;
        private set => SetProperty(ref _isOfflineBannerVisible, value);
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
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand OpenFilterCommand { get; }
    public ICommand OpenWarehouseCommand { get; }

    private void ClearSearch() => SearchText = "";

    private void SelectProduct(CatalogProductTileVm? product)
    {
        if (product is null)
            return;
        _onProductSelected?.Invoke(product);
    }

    private async Task ToggleFavoriteAsync(CatalogProductTileVm? product)
    {
        if (product is null)
            return;

        var newState = !product.IsFavorite;
        product.IsFavorite = newState;
        CatalogCacheService.SetFavorite(product.Id, newState);
        ApplyFilter();

        if (_catalogApi is null)
        {
            _prompts?.ShowToast(
                newState ? "Добавлено в избранное (локально)" : "Убрано из избранного (локально)");
            return;
        }

        try
        {
            var synced = await _catalogApi
                .SetProductFavoriteAsync(product.Id, newState)
                .ConfigureAwait(true);
            if (synced)
            {
                _auditDb?.LogFavorite(product.Id, newState);
                _prompts?.ShowToast(
                    newState ? "Добавлено в избранное на сайте" : "Убрано из избранного на сайте");
            }
            else
            {
                _prompts?.ShowToast("Избранное сохранено локально (сайт не ответил)", isWarning: true);
            }
        }
        catch (ApiException ex)
        {
            _prompts?.ShowToast($"Синхронизация избранного: {ex.Message}", isWarning: true);
        }
        catch (HttpRequestException)
        {
            _prompts?.ShowToast("Избранное сохранено локально (нет сети)", isWarning: true);
        }
    }

    private async Task RefreshCatalogAsync()
    {
        await _dispatcher.InvokeAsync(() => IsLoading = true).ConfigureAwait(false);

        try
        {
            // 1) Мгновенно показываем SQLite.
            var loadedFromDb = _catalogCache.TryLoadFromDatabase();
            var localProducts = _catalogCache.GetProducts().ToList();

            await _dispatcher.InvokeAsync(() =>
            {
                PublishProducts(localProducts);
                if (localProducts.Count > 0)
                {
                    StatusText = $"Каталог из локальной БД ({localProducts.Count}). Проверка обновлений…";
                    IsOfflineBannerVisible = false;
                }
                else
                {
                    StatusText = "Локальный каталог пуст. Загрузка с сервера…";
                }
            }).ConfigureAwait(false);

            var online = await IsOnlineAsync().ConfigureAwait(false);
            if (!online)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    IsOfflineBannerVisible = true;
                    StatusText = localProducts.Count > 0
                        ? $"Работа в автономном режиме (из локальной БД). Товаров: {localProducts.Count}."
                        : "Нет подключения и локальный каталог пуст. Проверьте сеть и нажмите «Обновить».";
                }).ConfigureAwait(false);
                return;
            }

            // 2) Сеть доступна — синхронизация с API + upsert в SQLite.
            await _dispatcher.InvokeAsync(() =>
                StatusText = localProducts.Count > 0
                    ? "Синхронизация каталога с сервером…"
                    : "Загрузка каталога с сервера…").ConfigureAwait(false);

            var syncResult = await _catalogCache.SyncCatalogFullAsync().ConfigureAwait(false);
            _catalogCache.TryLoadFromDatabase();
            var products = _catalogCache.GetProducts().ToList();

            await _dispatcher.InvokeAsync(() =>
            {
                PublishProducts(products);

                if (syncResult.Success)
                {
                    IsOfflineBannerVisible = false;
                    StatusText = products.Count > 0
                        ? $"Каталог обновлён. Товаров: {products.Count}."
                        : "Каталог на сервере пуст.";
                }
                else
                {
                    IsOfflineBannerVisible = products.Count > 0;
                    StatusText = products.Count > 0
                        ? $"Сервер недоступен — показан локальный каталог ({products.Count}). {syncResult.ErrorMessage}"
                        : syncResult.ErrorMessage ?? "Не удалось загрузить каталог.";
                }
            }).ConfigureAwait(false);

            PosLogger.Log(
                $"CATALOG refresh done: localWas={loadedFromDb}, online={online}, " +
                $"success={syncResult.Success}, count={products.Count}",
                "CATALOG");
        }
        catch (Exception ex)
        {
            PosLogger.Log($"CATALOG refresh failed: {ex}", "CATALOG");
            await _dispatcher.InvokeAsync(() =>
            {
                IsOfflineBannerVisible = _allProducts.Count > 0;
                StatusText = _allProducts.Count > 0
                    ? $"Ошибка синхронизации — показан локальный каталог ({_allProducts.Count})."
                    : "Ошибка загрузки каталога.";
            }).ConfigureAwait(false);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    private void PublishProducts(List<CatalogProductTileVm> products)
    {
        _allProducts = products;
        ApplyFilter();
        ProductCountText = $"Товаров: {_allProducts.Count}";
    }

    private async Task<bool> IsOnlineAsync()
    {
        try
        {
            return await _connectivity.IsOnlineAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"Connectivity check failed: {ex}", "NETWORK");
            return false;
        }
    }

    private void ApplyFilter()
    {
        Products.Clear();
        var query = _searchText.Trim();

        foreach (var product in _allProducts
                     .Where(MatchesTab)
                     .Where(p => MatchesSearch(p, query))
                     .OrderByDescending(p => p.IsFavorite)
                     .ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase))
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
/// Вкладка фильтра каталога (все / весовые / штучные).
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
