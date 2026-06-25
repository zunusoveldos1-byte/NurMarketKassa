using LinqToDB;
using NurMarketKassa.Configuration;
using NurMarketKassa.Data;
using NurMarketKassa.Models;
using NurMarketKassa.Models.Pos;

namespace NurMarketKassa.Services;

public sealed class ProductSearchResult
{
    public IReadOnlyList<CatalogProductTileVm> Items { get; init; } = Array.Empty<CatalogProductTileVm>();

    public bool HasMore { get; init; }
}

/// <summary>
/// Поиск товаров: приоритет — полный in-memory кэш локальной БД (100% каталога),
/// PostgreSQL — только если локальный кэш ещё не готов.
/// </summary>
public sealed class ProductSearchService
{
    private readonly PostgreSqlSettings _postgres;
    private readonly LocalProductRepository _localCatalog = LocalProductRepository.Instance;

    public ProductSearchService(PostgreSqlSettings postgres) => _postgres = postgres;

    private bool UsePostgreSql =>
        _postgres.Enabled && !string.IsNullOrWhiteSpace(_postgres.ConnectionString);

    public Task WarmUpLocalCacheAsync(CancellationToken cancellationToken = default) =>
        _localCatalog.WarmUpCacheAsync(cancellationToken);

    public async Task<Product?> FindProductByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return null;

        var cleanBarcode = barcode.Trim();

        await _localCatalog.EnsureCacheReadyAsync(cancellationToken).ConfigureAwait(false);
        var cachedTile = _localCatalog.TryGetTileByBarcode(cleanBarcode);
        if (cachedTile != null)
            return await _localCatalog.GetByBarcodeAsync(cleanBarcode).ConfigureAwait(false);

        if (!UsePostgreSql)
            return null;

        try
        {
            await using var db = new AppDataConnection(_postgres);
            var lowered = cleanBarcode.ToLowerInvariant();
            var found = await db.Products
                .FirstOrDefaultAsync(
                    p => p.Barcode != null && p.Barcode.ToLower() == lowered,
                    cancellationToken)
                .ConfigureAwait(false);
            PersistPostgresConnectionIfNeeded();
            return found;
        }
        catch (Exception ex)
        {
            PosLogger.Log($"PostgreSQL barcode lookup failed: {ex.Message}", "CATALOG");
            return null;
        }
    }

    /// <summary>Универсальный поиск для добавления в чек: штрихкод, название, артикул/id.</summary>
    public async Task<ProductLookupResult> LookupProductsAsync(
        string rawQuery,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var query = NormalizeLookupQuery(rawQuery);
        if (string.IsNullOrEmpty(query))
            return ProductLookupResult.Empty;

        limit = Math.Clamp(limit, 1, 100);
        PosLogger.Log($"[PRODUCT SEARCH] query=\"{query}\"", "SEARCH");

        await _localCatalog.EnsureCacheReadyAsync(cancellationToken).ConfigureAwait(false);

        var exactBarcode = _localCatalog.TryGetTileByBarcode(query);
        if (exactBarcode != null)
            return SingleLookupResult(exactBarcode, ProductMatchField.Barcode, "barcode (exact cache)");

        var exactSku = _localCatalog.TryGetTileBySku(query);
        if (exactSku != null)
            return SingleLookupResult(exactSku, ProductMatchField.Id, "sku/id (exact cache)");

        var candidates = _localCatalog.SearchAllMatches(query);

        var scored = ScoreLookupMatches(query, candidates);
        if (scored.Count == 0)
        {
            PosLogger.Log("[PRODUCT SEARCH] found 0 items after full-cache lookup", "SEARCH");
            return ProductLookupResult.Empty;
        }

        var trimmed = scored.Take(limit).ToList();
        var best = PickBestLookupMatch(trimmed);
        PosLogger.Log(
            $"[PRODUCT SEARCH] full-cache total={scored.Count}, returned={trimmed.Count}, best={(best == null ? "ambiguous" : best.Tile.Title)}",
            "SEARCH");

        return new ProductLookupResult { Items = trimmed, BestMatch = best };
    }

    private static ProductLookupResult SingleLookupResult(
        CatalogProductTileVm tile,
        ProductMatchField field,
        string logTag)
    {
        var item = new ProductLookupItem
        {
            Tile = tile,
            MatchField = field,
            Score = 1000,
        };
        PosLogger.Log($"[PRODUCT SEARCH] found 1 item via {logTag}", "SEARCH");
        return new ProductLookupResult { Items = [item], BestMatch = item };
    }

    internal static string NormalizeLookupQuery(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();

    internal static List<ProductLookupItem> ScoreLookupMatches(
        string query,
        IReadOnlyList<CatalogProductTileVm> tiles)
    {
        var q = query.ToLowerInvariant();
        var list = new List<ProductLookupItem>(tiles.Count);
        foreach (var tile in tiles)
        {
            var (field, score) = ScoreTile(tile, q);
            if (score > 0)
            {
                list.Add(new ProductLookupItem
                {
                    Tile = tile,
                    MatchField = field,
                    Score = score,
                });
            }
        }

        return list
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Tile.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static (ProductMatchField Field, int Score) ScoreTile(CatalogProductTileVm tile, string q)
    {
        var barcode = tile.Barcode?.Trim().ToLowerInvariant() ?? string.Empty;
        var name = tile.Title.Trim().ToLowerInvariant();
        var id = tile.Id.Trim().ToLowerInvariant();

        if (barcode.Length > 0 && barcode == q)
            return (ProductMatchField.Barcode, 900);
        if (id == q)
            return (ProductMatchField.Id, 850);
        if (name == q)
            return (ProductMatchField.Name, 800);
        if (name.StartsWith(q, StringComparison.Ordinal))
            return (ProductMatchField.Name, 600);
        if (barcode.Length > 0 && barcode.StartsWith(q, StringComparison.Ordinal))
            return (ProductMatchField.Barcode, 550);
        if (id.StartsWith(q, StringComparison.Ordinal))
            return (ProductMatchField.Id, 500);
        if (name.Contains(q, StringComparison.Ordinal))
            return (ProductMatchField.Name, 400);
        if (barcode.Length > 0 && barcode.Contains(q, StringComparison.Ordinal))
            return (ProductMatchField.Barcode, 350);
        if (id.Contains(q, StringComparison.Ordinal))
            return (ProductMatchField.Id, 300);

        return (ProductMatchField.None, 0);
    }

    private static ProductLookupItem? PickBestLookupMatch(IReadOnlyList<ProductLookupItem> items)
    {
        if (items.Count == 0)
            return null;
        if (items.Count == 1)
            return items[0];

        var top = items[0];
        var second = items[1];
        if (top.Score >= 800)
            return top;
        if (top.Score - second.Score >= 150)
            return top;
        if (top.Score >= 600 && second.Score < 500)
            return top;

        return null;
    }

    public async Task<ProductSearchResult> SearchProductsAsync(
        string searchText,
        bool isWeightCategory,
        int currentOffset = 0,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);

        if (await TrySearchLocalCacheAsync(searchText, isWeightCategory, currentOffset, pageSize, cancellationToken)
                .ConfigureAwait(false) is { } local)
            return local;

        if (!UsePostgreSql)
            return EmptyResult();

        try
        {
            return await SearchPostgreSqlAsync(
                searchText,
                isWeightCategory,
                currentOffset,
                pageSize,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"PostgreSQL search failed: {ex.Message}", "CATALOG");
            return EmptyResult();
        }
    }

    public async Task<ProductSearchResult> LoadAllProductsPageAsync(
        int offset,
        int pageSize,
        FilterCriteria? filter = null,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        offset = Math.Max(0, offset);

        if (await TryBrowseLocalCacheAsync(null, offset, pageSize, filter, cancellationToken).ConfigureAwait(false) is { } local)
            return local;

        if (!UsePostgreSql)
            return EmptyResult();

        try
        {
            return await LoadAllProductsPostgreSqlAsync(offset, pageSize, filter, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"PostgreSQL all-products page failed: {ex.Message}", "CATALOG");
            return EmptyResult();
        }
    }

    public async Task<ProductSearchResult> SearchAllProductsAsync(
        string searchText,
        int offset,
        int pageSize,
        FilterCriteria? filter = null,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        offset = Math.Max(0, offset);

        if (await TryBrowseLocalCacheAsync(searchText, offset, pageSize, filter, cancellationToken).ConfigureAwait(false) is { } local)
            return local;

        if (!UsePostgreSql)
            return EmptyResult();

        try
        {
            return await SearchAllProductsPostgreSqlAsync(
                searchText,
                offset,
                pageSize,
                filter,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"PostgreSQL all-products search failed: {ex.Message}", "CATALOG");
            return EmptyResult();
        }
    }

    private async Task<ProductSearchResult?> TrySearchLocalCacheAsync(
        string searchText,
        bool isWeightCategory,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await _localCatalog.EnsureCacheReadyAsync(cancellationToken).ConfigureAwait(false);
        if (!_localCatalog.IsCacheReady)
            return null;

        var (items, hasMore) = _localCatalog.SearchCache(
            searchText,
            isWeightCategory,
            criteria: null,
            offset,
            pageSize);

        return new ProductSearchResult { Items = items, HasMore = hasMore };
    }

    private async Task<ProductSearchResult?> TryBrowseLocalCacheAsync(
        string? searchText,
        int offset,
        int pageSize,
        FilterCriteria? filter,
        CancellationToken cancellationToken)
    {
        await _localCatalog.EnsureCacheReadyAsync(cancellationToken).ConfigureAwait(false);
        if (!_localCatalog.IsCacheReady)
            return null;

        var (items, hasMore) = _localCatalog.SearchCache(
            searchText,
            mustWeigh: null,
            filter,
            offset,
            pageSize);

        return new ProductSearchResult { Items = items, HasMore = hasMore };
    }

    private static ProductSearchResult EmptyResult() =>
        new() { Items = Array.Empty<CatalogProductTileVm>(), HasMore = false };

    private async Task<ProductSearchResult> SearchPostgreSqlAsync(
        string searchText,
        bool isWeightCategory,
        int currentOffset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var db = new AppDataConnection(_postgres);

        IQueryable<Models.Product> query = db.Products.Where(p => p.IsWeight == isWeightCategory);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var term = searchText.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(term)) ||
                p.Id.ToLower().Contains(term));
        }

        var loaded = await query
            .OrderBy(p => p.Name)
            .Skip(currentOffset)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        PersistPostgresConnectionIfNeeded();

        return new ProductSearchResult
        {
            Items = MapProductsToTiles(loaded),
            HasMore = loaded.Count == pageSize,
        };
    }

    private async Task<ProductSearchResult> LoadAllProductsPostgreSqlAsync(
        int offset,
        int pageSize,
        FilterCriteria? filter,
        CancellationToken cancellationToken)
    {
        await using var db = new AppDataConnection(_postgres);
        var query = ApplyPostgreSqlFilters(db.Products.AsQueryable(), null, filter);

        var loaded = await query
            .OrderBy(p => p.Name)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        PersistPostgresConnectionIfNeeded();

        return new ProductSearchResult
        {
            Items = MapProductsToTiles(loaded),
            HasMore = loaded.Count == pageSize,
        };
    }

    private async Task<ProductSearchResult> SearchAllProductsPostgreSqlAsync(
        string searchText,
        int offset,
        int pageSize,
        FilterCriteria? filter,
        CancellationToken cancellationToken)
    {
        await using var db = new AppDataConnection(_postgres);
        var query = ApplyPostgreSqlFilters(db.Products.AsQueryable(), searchText, filter);

        var loaded = await query
            .OrderBy(p => p.Name)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        PersistPostgresConnectionIfNeeded();

        return new ProductSearchResult
        {
            Items = MapProductsToTiles(loaded),
            HasMore = loaded.Count == pageSize,
        };
    }

    private static IQueryable<Models.Product> ApplyPostgreSqlFilters(
        IQueryable<Models.Product> query,
        string? searchText,
        FilterCriteria? filter)
    {
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var term = searchText.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(term)) ||
                p.Id.ToLower().Contains(term));
        }
        else if (!string.IsNullOrWhiteSpace(filter?.SearchQuery))
        {
            var term = filter.SearchQuery.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(term)) ||
                p.Id.ToLower().Contains(term));
        }

        if (filter == null)
            return query;

        if (filter.PriceMin.HasValue)
            query = query.Where(p => p.Price >= (decimal)filter.PriceMin.Value);
        if (filter.PriceMax.HasValue)
            query = query.Where(p => p.Price <= (decimal)filter.PriceMax.Value);
        if (!string.IsNullOrWhiteSpace(filter.Category))
            query = query.Where(p => p.Category == filter.Category);
        if (!string.IsNullOrWhiteSpace(filter.Brand))
            query = query.Where(p => p.Brand == filter.Brand);
        if (filter.OnlyFavorite)
            query = query.Where(p => p.IsFavorite);
        if (filter.OnlyWeight)
            query = query.Where(p => p.IsWeight);
        if (filter.OnlyPiece)
            query = query.Where(p => !p.IsWeight);

        return query;
    }

    private static List<CatalogProductTileVm> MapProductsToTiles(List<Models.Product> loaded)
    {
        var tiles = new List<CatalogProductTileVm>(loaded.Count);
        foreach (var product in loaded)
        {
            if (product.Quantity == null)
                product.Quantity = 0;

            var tile = ProductEntityMapper.ToTile(product);
            if (tile != null)
                tiles.Add(tile);
        }

        return tiles;
    }

    private void PersistPostgresConnectionIfNeeded() =>
        PostgreSqlConnectionStringResolver.PersistIfNeeded(
            _postgres.ConnectionString,
            UserPreferences.Instance);
}
