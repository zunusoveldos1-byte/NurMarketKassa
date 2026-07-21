using System.Collections.ObjectModel;
using System.Globalization;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Models;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Ui.Shared;

namespace NurMarketKassa.ViewModels.Catalog;

public sealed class CatalogViewModel : ViewModelBase, IDisposable
{
    private const int SearchDebounceMs = 275;

    private readonly ICatalogCacheService _catalogCache;
    private readonly IDispatcher _dispatcher;
    private readonly IAppSession _session;
    private readonly IConnectivityService _connectivity;

    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _searchDebounceCts;
    private bool _catalogLoadBusy;
    private string _searchText = "";
    private CategoryDto? _selectedCategory;
    private FilterCriteria? _activeFilter;
    private bool _isCatalogLoading;

    public CatalogViewModel(
        ICatalogCacheService catalogCache,
        IDispatcher dispatcher,
        IAppSession session,
        IConnectivityService connectivity)
    {
        _catalogCache = catalogCache;
        _dispatcher = dispatcher;
        _session = session;
        _connectivity = connectivity;
    }

    public ObservableCollection<CatalogProductTileVm> RemoteTiles { get; } = new();

    public ObservableCollection<CatalogProductTileVm> FilteredTiles { get; } = new();

    public ObservableCollection<CategoryDto> Categories { get; } = new();

    public CategoryDto? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!SetProperty(ref _selectedCategory, value))
                return;
            _ = RunCatalogSearchAsync();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? ""))
                return;
            ScheduleDebouncedSearch();
        }
    }

    public bool IsCatalogLoading
    {
        get => _isCatalogLoading;
        private set => SetProperty(ref _isCatalogLoading, value);
    }

    public FilterCriteria? ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (!SetProperty(ref _activeFilter, value))
                return;
            _ = RunCatalogSearchAsync();
        }
    }

    public event Action<CatalogLoadCompletedEventArgs>? LoadCompleted;

    public async Task LoadCatalogAsync(CancellationToken cancellationToken = default, bool manual = false)
    {
        if (_catalogLoadBusy)
            return;

        if (await ShouldUseLocalCatalogAsync(cancellationToken).ConfigureAwait(false))
        {
            var restored = TryRestoreFromCache();
            LoadCompleted?.Invoke(new CatalogLoadCompletedEventArgs
            {
                Success = restored,
                RestoredFromCache = restored,
                ProductCount = RemoteTiles.Count,
            });
            return;
        }

        _catalogLoadBusy = true;
        IsCatalogLoading = true;
        try
        {
            if (manual)
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);

            var result = await _catalogCache
                .SyncCatalogFullAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                var restored = TryRestoreFromCache();
                LoadCompleted?.Invoke(new CatalogLoadCompletedEventArgs
                {
                    Success = restored,
                    RestoredFromCache = restored,
                    ErrorMessage = result.ErrorMessage,
                    ProductCount = RemoteTiles.Count,
                });
                return;
            }

            ApplyCatalogData();
            LoadCompleted?.Invoke(new CatalogLoadCompletedEventArgs
            {
                Success = true,
                Added = result.Added,
                Changed = result.Changed,
                Deleted = result.Deleted,
                ProductCount = RemoteTiles.Count,
                ManualRefresh = manual,
            });
        }
        catch (TaskCanceledException)
        {
            var restored = TryRestoreFromCache();
            LoadCompleted?.Invoke(new CatalogLoadCompletedEventArgs
            {
                Success = restored,
                RestoredFromCache = restored,
                ErrorMessage = restored ? null : "Каталог: таймаут.",
                ProductCount = RemoteTiles.Count,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var restored = TryRestoreFromCache();
            LoadCompleted?.Invoke(new CatalogLoadCompletedEventArgs
            {
                Success = restored,
                RestoredFromCache = restored,
                ErrorMessage = ex.Message,
                ProductCount = RemoteTiles.Count,
            });
        }
        finally
        {
            _catalogLoadBusy = false;
            IsCatalogLoading = false;
        }
    }

    public void RefreshCategoriesAndBrands()
    {
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var vm in RemoteTiles)
        {
            if (!string.IsNullOrWhiteSpace(vm.Category))
                categories.Add(vm.Category);
        }

        _dispatcher.InvokeAsync(() =>
        {
            Categories.Clear();
            foreach (var name in categories.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                Categories.Add(new CategoryDto { Name = name });
        });
    }

    public Task RunCatalogSearchAsync() =>
        _dispatcher.InvokeAsync(ApplyFilterToFilteredTiles);

    public bool FilterPredicate(CatalogProductTileVm vm)
    {
        if (_activeFilter != null)
        {
            if (_activeFilter.DateFrom.HasValue && vm.CreatedAt < _activeFilter.DateFrom.Value)
                return false;
            if (_activeFilter.DateTo.HasValue && vm.CreatedAt > _activeFilter.DateTo.Value)
                return false;

            if (!string.IsNullOrEmpty(_activeFilter.Category) &&
                !string.Equals(vm.Category ?? "", _activeFilter.Category, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(_activeFilter.Brand) &&
                !string.Equals(vm.Brand ?? "", _activeFilter.Brand, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(_activeFilter.Client) &&
                !string.Equals(vm.ClientName ?? "", _activeFilter.Client, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(_activeFilter.Status) &&
                !string.Equals(vm.Status ?? "", _activeFilter.Status, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(_activeFilter.HotkeyGroup) &&
                !string.Equals(vm.HotkeyGroup ?? "", _activeFilter.HotkeyGroup, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(_activeFilter.SearchQuery))
            {
                var query = _activeFilter.SearchQuery.Trim().ToLowerInvariant();
                if (!MatchesSearchQuery(vm, query))
                    return false;
            }

            if (_activeFilter.OnlyWeight && !vm.MustWeigh)
                return false;
            if (_activeFilter.OnlyPiece && vm.MustWeigh)
                return false;
            if (_activeFilter.OnlyInStock && vm.Quantity <= 0)
                return false;
            if (_activeFilter.OnlyFavorite && !vm.IsFavorite)
                return false;

            var price = TryParseTilePrice(vm.PriceLine);
            if (_activeFilter.PriceMin.HasValue && price < _activeFilter.PriceMin.Value)
                return false;
            if (_activeFilter.PriceMax.HasValue && price > _activeFilter.PriceMax.Value)
                return false;
        }

        if (_selectedCategory != null &&
            !string.Equals(vm.Category ?? "", _selectedCategory.Name, StringComparison.OrdinalIgnoreCase))
            return false;

        var text = _searchText.Trim();
        if (text.Length >= 2 && !MatchesSearchQuery(vm, text.ToLowerInvariant()))
            return false;

        return true;
    }

    public void Dispose()
    {
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
    }

    private async Task<bool> ShouldUseLocalCatalogAsync(CancellationToken cancellationToken)
    {
        if (_session.IsOfflineBootstrap)
            return true;

        try
        {
            return !await _connectivity.IsOnlineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return true;
        }
    }

    private bool TryRestoreFromCache()
    {
        if (!_catalogCache.TryLoadFromDatabase())
            return false;

        ApplyCatalogData();
        return RemoteTiles.Count > 0;
    }

    private void ApplyCatalogData()
    {
        var products = _catalogCache.GetProducts();
        _dispatcher.InvokeAsync(() =>
        {
            RemoteTiles.Clear();
            foreach (var tile in products)
                RemoteTiles.Add(tile);
            RefreshCategoriesAndBrands();
            ApplyFilterToFilteredTiles();
        });
    }

    private void ApplyFilterToFilteredTiles()
    {
        FilteredTiles.Clear();
        foreach (var tile in RemoteTiles.Where(FilterPredicate))
            FilteredTiles.Add(tile);
    }

    private void ScheduleDebouncedSearch()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var token = _searchDebounceCts.Token;
        _ = DebounceSearchAsync(token);
    }

    private async Task DebounceSearchAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, token).ConfigureAwait(false);
            await RunCatalogSearchAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            /* superseded keystroke */
        }
    }

    private static bool MatchesSearchQuery(CatalogProductTileVm vm, string query)
    {
        var title = vm.Title.ToLowerInvariant();
        var barcode = vm.Barcode?.ToLowerInvariant() ?? string.Empty;
        var id = vm.Id.ToLowerInvariant();
        return title.Contains(query) || barcode.Contains(query) || id.Contains(query);
    }

    private static double TryParseTilePrice(string? priceLine)
    {
        if (string.IsNullOrWhiteSpace(priceLine))
            return 0;

        var digits = new string(priceLine
            .TakeWhile(ch => char.IsDigit(ch) || ch is '.' or ',')
            .ToArray())
            .Replace(',', '.');

        return double.TryParse(digits, NumberStyles.Any, CultureInfo.InvariantCulture, out var price)
            ? price
            : 0;
    }
}

public sealed class CatalogLoadCompletedEventArgs
{
    public bool Success { get; init; }

    public bool RestoredFromCache { get; init; }

    public int Added { get; init; }

    public int Changed { get; init; }

    public int Deleted { get; init; }

    public string? ErrorMessage { get; init; }

    public int ProductCount { get; init; }

    public bool ManualRefresh { get; init; }
}
