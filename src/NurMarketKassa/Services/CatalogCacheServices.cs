using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;
using NurMarketKassa.Views;
using System.Linq;

#nullable enable
namespace NurMarketKassa.Services
{
    public static class CatalogCacheService
    {
        private static readonly string CacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        private static readonly string CacheFilePath = Path.Combine(CacheDirectory, "products_cache.json");
        private static readonly TimeSpan BackgroundSyncInterval = TimeSpan.FromMinutes(10.0);
        private static CancellationTokenSource? _backgroundCts;
        private static Task? _backgroundTask;

        public static ObservableCollection<CatalogProductTileVm> Products { get; } = new ObservableCollection<CatalogProductTileVm>();

        public static DateTime? LastSyncTime { get; private set; }

        public static async Task InitializeAsync()
        {
            if (!LoadFromFile())
            {
                ShowToastInMainWindow("Загрузка данных из облака...", false);
                await RefreshFromApiAsync();
            }
            else
            {
                ShowToastInMainWindow("Каталог загружен из локального кэша", false);
            }

            StartBackgroundSync();
        }

        public static async Task RefreshFromApiAsync()
        {
            try
            {
                var rawItems = await App.Api.ProductsCatalogAsync(
                    App.Settings.Catalog.QuickCatalogLimit,
                    App.Settings.Catalog.CatalogMaxPages);

                string apiBaseUrl = App.Settings.ApiBaseUrl;
                var newList = new List<CatalogProductTileVm>();

                foreach (JsonElement el in rawItems)
                {
                    var vm = ProductCatalogMapper.TryTile(el, apiBaseUrl);
                    if (vm != null)
                        newList.Add(vm);
                }

                // Единый вызов UI-потока
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Products.Clear();
                    foreach (var vm in newList)
                        Products.Add(vm);

                    // Обновляем индикатор кэша, если главное окно доступно
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                        mainWindow.UpdateCacheStatus();
                });

                SaveToFile();
                LastSyncTime = DateTime.UtcNow;
            }
            catch
            {
                ShowToastInMainWindow("Ошибка загрузки каталога.", true);
            }
        }


        private static HashSet<string>? _favoriteIds;
        private static readonly string FavoritesFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "favorites.json");

        public static HashSet<string> FavoriteIds
        {
            get
            {
                if (_favoriteIds == null)
                    LoadFavoriteIds();
                return _favoriteIds!;
            }
        }

        public static void LoadFavoriteIds()
        {
            try
            {
                if (File.Exists(FavoritesFilePath))
                {
                    var json = File.ReadAllText(FavoritesFilePath);
                    _favoriteIds = JsonSerializer.Deserialize<HashSet<string>>(json) ?? new HashSet<string>();
                }
                else
                {
                    _favoriteIds = new HashSet<string>();
                }
            }
            catch
            {
                _favoriteIds = new HashSet<string>();
            }
        }

        public static void SaveFavoriteIds()
        {
            try
            {
                var json = JsonSerializer.Serialize(FavoriteIds);
                File.WriteAllText(FavoritesFilePath, json);
            }
            catch { /* игнорируем */ }
        }

        public static bool LoadFromFile()
        {
            try
            {
                if (!File.Exists(CacheFilePath))
                    return false;

                string json = File.ReadAllText(CacheFilePath);
                var list = JsonSerializer.Deserialize<List<CatalogProductTileVm>>(json);

                if (list == null)
                    return false;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Products.Clear();
                    foreach (var vm in list)
                        Products.Add(vm);
                });

                LastSyncTime = File.GetLastWriteTimeUtc(CacheFilePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // В начале класса CatalogCacheService
        private static readonly JsonSerializerOptions PrettyJsonOptions = new()
        {
            WriteIndented = true
        };

        // Метод SaveToFile
        public static void SaveToFile()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                    Directory.CreateDirectory(CacheDirectory);

                var list = Products.ToList();
                string json = JsonSerializer.Serialize(list, PrettyJsonOptions);
                File.WriteAllText(CacheFilePath, json);
            }
            catch { /* игнорируем */ }
        }
        private static void StartBackgroundSync()
        {
            StopBackgroundSync();
            _backgroundCts = new CancellationTokenSource();
            CancellationToken token = _backgroundCts.Token;

            _backgroundTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(BackgroundSyncInterval, token);
                        await RefreshFromApiAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // ignore background errors
                    }
                }
            }, token);
        }

        public static void StopBackgroundSync()
        {
            if (_backgroundCts != null)
            {
                _backgroundCts.Cancel();
                _backgroundCts.Dispose();
                _backgroundCts = null;
            }
        }

        private static void ShowToastInMainWindow(string message, bool isWarning)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.ShowToast(message, isWarning);
                }
            });
        }
    }
}