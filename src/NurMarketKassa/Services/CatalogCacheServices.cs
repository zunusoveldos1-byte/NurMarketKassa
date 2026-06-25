using System.Collections.ObjectModel;

using System.Net.Http;

using System.Text.Json;

using System.Windows;

using NurMarketKassa.Models;

using NurMarketKassa.Models.Pos;

using NurMarketKassa.Views;



#nullable enable



namespace NurMarketKassa.Services;



public static class CatalogCacheService

{

    private static readonly LocalProductRepository Repository = LocalProductRepository.Instance;



    public static ObservableCollection<CatalogProductTileVm> Products { get; } = new();



    public static DateTime? LastSyncTime { get; private set; }



    public static string? LocalCatalogVersionToken => Repository.GetCatalogVersionToken();



    public static void EnsureLocalDatabase()

    {

        Repository.EnsureSchema();

    }



    public static HashSet<string> FavoriteIds => Repository.GetFavoriteIds();



    public static void SetFavorite(string productId, bool isFavorite)

    {

        Repository.SetFavorite(productId, isFavorite);

    }



    public static bool LoadFromDatabase()

    {

        try

        {

            var tiles = Repository.LoadAllTiles();

            SetProducts(tiles);

            LastSyncTime = Repository.GetLastSyncTime();

            return tiles.Count > 0;

        }

        catch

        {

            return false;

        }

    }



    public static void SetProducts(IEnumerable<CatalogProductTileVm> products)

    {

        Application.Current.Dispatcher.Invoke(() =>

        {

            Products.Clear();

            foreach (var vm in products)

                Products.Add(vm);

        });

    }



    /// <summary>Очищает in-memory коллекцию каталога (без обращения к SQLite).</summary>

    public static void ClearInMemory()

    {

        Application.Current.Dispatcher.Invoke(() => Products.Clear());

        LastSyncTime = null;

    }



    public static IReadOnlyList<CatalogProductTileVm> ApplySqlFilter(FilterCriteria criteria)

    {

        var tiles = Repository.QueryFiltered(criteria);

        SetProducts(tiles);

        return tiles;

    }



    public static async Task<CatalogVersionInfo?> FetchRemoteVersionAsync(CancellationToken cancellationToken = default)

    {

        return await App.CatalogApi.ProductsCatalogVersionAsync(cancellationToken).ConfigureAwait(false);

    }



    public static void SaveLocalVersionToken(string token) =>
        Repository.SetCatalogVersionToken(token);

    public static bool IsSameVersion(CatalogVersionInfo remote)

    {

        var local = LocalCatalogVersionToken;

        if (string.IsNullOrWhiteSpace(local))

            return false;

        return string.Equals(local, remote.Token, StringComparison.Ordinal);

    }



    public static async Task<CatalogSyncResult> SyncCatalogFullAsync(CancellationToken cancellationToken = default)

    {

        if (OfflineModeHelper.UseLocalOperations)

            return CatalogSyncResult.Failed("Нет подключения — каталог из локальной базы.");



        try

        {

            var remoteVersion = await FetchRemoteVersionAsync(cancellationToken).ConfigureAwait(false);



            var rawItems = await App.CatalogApi.ProductsCatalogAsync(

                App.Settings.Catalog.QuickCatalogLimit,

                App.Settings.Catalog.CatalogMaxPages,

                cancellationToken).ConfigureAwait(false);



            string apiBaseUrl = App.Settings.ApiBaseUrl;

            var newList = new List<CatalogProductTileVm>();



            foreach (JsonElement el in rawItems)

            {

                var vm = ProductCatalogMapper.TryTile(el, apiBaseUrl);

                if (vm != null)

                    newList.Add(vm);

            }



            await StockSyncService.OverlayAgentStockAsync(newList, cancellationToken).ConfigureAwait(false);

            var (added, changed, deleted) = Repository.SyncReplaceAllWithDiff(newList);



            if (remoteVersion != null && !remoteVersion.IsEmpty)

                Repository.SetCatalogVersionToken(remoteVersion.Token);



            LastSyncTime = DateTime.UtcNow;

            Repository.SetLastSyncTime(LastSyncTime.Value);

            App.AuditDb.LogEvent("catalog", "refresh", new { count = newList.Count, added, changed, deleted });



            PosLogger.Log(

                $"CATALOG sync: added={added}, changed={changed}, deleted={deleted}, total={newList.Count}, version={remoteVersion?.Token}",

                "CATALOG");



            await Application.Current.Dispatcher.InvokeAsync(() =>

            {

                Products.Clear();

                foreach (var vm in newList)

                    Products.Add(vm);



                if (Application.Current.MainWindow is MainWindow mainWindow)

                    mainWindow.UpdateCacheStatus();

            });



            return CatalogSyncResult.Ok(added, changed, deleted, remoteVersion);

        }

        catch (OperationCanceledException)

        {

            throw;

        }

        catch (ApiException ex)

        {

            return CatalogSyncResult.Failed(ex.Message);

        }

        catch (HttpRequestException ex)

        {

            return CatalogSyncResult.Failed(string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message);

        }

        catch (Exception ex)

        {

            return CatalogSyncResult.Failed(ex.Message);

        }

    }



    /// <summary>Обратная совместимость — полная синхронизация.</summary>

    public static async Task RefreshFromApiAsync(CancellationToken cancellationToken = default)

    {

        var result = await SyncCatalogFullAsync(cancellationToken).ConfigureAwait(false);

        if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))

            ShowToastInMainWindow(result.ErrorMessage, true);

    }



    public static void PersistProductStock(string productId, double quantity, bool mustWeigh)

    {

        Repository.UpdateStock(productId, quantity, mustWeigh);

    }



    private static void ShowToastInMainWindow(string message, bool isWarning)

    {

        Application.Current.Dispatcher.Invoke(() =>

        {

            if (Application.Current.MainWindow is MainWindow mainWindow)

                mainWindow.ShowToast(message, isWarning);

        });

    }

}


