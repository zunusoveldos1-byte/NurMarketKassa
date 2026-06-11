using NurMarketKassa.Models;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NurMarketKassa.Views
{
    public partial class FilterWindow : Window
    {
        /// <summary>Результат фильтрации – готовый список плиток, полученный с сервера (null, если сервер недоступен).</summary>
        public List<CatalogProductTileVm>? FilteredTiles { get; private set; }
        /// <summary>Локальные критерии фильтрации (используются как fallback).</summary>
        public FilterCriteria? Result { get; private set; }

        private readonly NurMarketApiClient _api;

        public FilterWindow(NurMarketApiClient api,
                    IEnumerable<string> categories,
                    IEnumerable<string> brands)
        {
            InitializeComponent();
            _api = api ?? throw new ArgumentNullException(nameof(api));

            CategoryCombo.ItemsSource = categories?.ToList() ?? new List<string>();
            if (CategoryCombo.Items.Count == 0) CategoryCombo.IsEnabled = false;
            BrandCombo.ItemsSource = brands?.ToList() ?? new List<string>();
            if (BrandCombo.Items.Count == 0) BrandCombo.IsEnabled = false;

            LoadClientsAsync();
            LoadStatuses();
            LoadHotkeyGroups();

            RestoreFilterState();   // ← восстанавливаем последние настройки
        }

        private void RestoreFilterState()
        {
            var prefs = UserPreferences.Instance;

            DateFromPicker.SelectedDate = prefs.LastFilterDateFrom;
            DateToPicker.SelectedDate = prefs.LastFilterDateTo;

            if (!string.IsNullOrEmpty(prefs.LastFilterCategory))
                CategoryCombo.SelectedItem = prefs.LastFilterCategory;
            if (!string.IsNullOrEmpty(prefs.LastFilterBrand))
                BrandCombo.SelectedItem = prefs.LastFilterBrand;
            if (!string.IsNullOrEmpty(prefs.LastFilterClient))
                ClientCombo.SelectedItem = prefs.LastFilterClient;
            if (!string.IsNullOrEmpty(prefs.LastFilterStatus))
                StatusCombo.SelectedItem = prefs.LastFilterStatus;
            if (!string.IsNullOrEmpty(prefs.LastFilterHotkeyGroup))
                HotkeyGroupCombo.SelectedItem = prefs.LastFilterHotkeyGroup;

            WeightCheck.IsChecked = prefs.LastFilterOnlyWeight;
            InStockCheck.IsChecked = prefs.LastFilterOnlyInStock;
            FavoriteCheck.IsChecked = prefs.LastFilterOnlyFavorite;
        }

        private async void LoadClientsAsync()
        {
            try
            {
                var clients = await _api.GetAgentProductsAsync(CancellationToken.None);
                var names = clients
                    .Select(c => c.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                ClientCombo.ItemsSource = names;
                if (names.Count == 0) ClientCombo.IsEnabled = false;

                // Восстанавливаем сохранённое значение после заполнения
                var prefs = UserPreferences.Instance;
                if (!string.IsNullOrEmpty(prefs.LastFilterClient))
                    ClientCombo.SelectedItem = prefs.LastFilterClient;
            }
            catch { /* тихо */ }
        }

        private void LoadStatuses()
        {
            var statuses = CatalogCacheService.Products
                .Select(p => p.StatusDisplay)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .OrderBy(s => s)
                .ToList();
            StatusCombo.ItemsSource = statuses;
            if (StatusCombo.Items.Count == 0) StatusCombo.IsEnabled = false;

            var prefs = UserPreferences.Instance;
            if (!string.IsNullOrEmpty(prefs.LastFilterStatus))
                StatusCombo.SelectedItem = prefs.LastFilterStatus;
        }

        private void LoadHotkeyGroups()
        {
            var groups = CatalogCacheService.Products
                .Select(p => p.HotkeyGroupName)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct()
                .OrderBy(g => g)
                .ToList();
            HotkeyGroupCombo.ItemsSource = groups;
            if (HotkeyGroupCombo.Items.Count == 0) HotkeyGroupCombo.IsEnabled = false;

            var prefs = UserPreferences.Instance;
            if (!string.IsNullOrEmpty(prefs.LastFilterHotkeyGroup))
                HotkeyGroupCombo.SelectedItem = prefs.LastFilterHotkeyGroup;
        }

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            // Формируем локальные критерии (для fallback)
            var localCriteria = new FilterCriteria
            {
                DateFrom = DateFromPicker.SelectedDate,
                DateTo = DateToPicker.SelectedDate,
                Category = CategoryCombo.SelectedItem as string,
                Brand = BrandCombo.SelectedItem as string,
                Client = ClientCombo.SelectedItem as string,
                Status = StatusCombo.SelectedItem as string,
                HotkeyGroup = HotkeyGroupCombo.SelectedItem as string,
                OnlyWeight = WeightCheck.IsChecked == true,
                OnlyInStock = InStockCheck.IsChecked == true,
                OnlyFavorite = FavoriteCheck.IsChecked == true
            };
            Result = localCriteria;

            // Сохраняем в настройках
            var prefs = UserPreferences.Instance;
            prefs.LastFilterDateFrom = DateFromPicker.SelectedDate;
            prefs.LastFilterDateTo = DateToPicker.SelectedDate;
            prefs.LastFilterCategory = CategoryCombo.SelectedItem as string;
            prefs.LastFilterBrand = BrandCombo.SelectedItem as string;
            prefs.LastFilterClient = ClientCombo.SelectedItem as string;
            prefs.LastFilterStatus = StatusCombo.SelectedItem as string;
            prefs.LastFilterHotkeyGroup = HotkeyGroupCombo.SelectedItem as string;
            prefs.LastFilterOnlyWeight = WeightCheck.IsChecked == true;
            prefs.LastFilterOnlyInStock = InStockCheck.IsChecked == true;
            prefs.LastFilterOnlyFavorite = FavoriteCheck.IsChecked == true;
            prefs.SaveToDisk();

            // Пытаемся загрузить отфильтрованный список с сервера
            try
            {
                var tiles = await FetchFilteredProductsAsync(localCriteria);
                if (tiles != null && tiles.Count > 0)
                {
                    FilteredTiles = tiles;   // основной результат – готовые плитки
                    DialogResult = true;
                    Close();
                    return;
                }
            }
            catch { /* сервер недоступен – остаёмся на локальной фильтрации */ }

            // Если сервер не ответил или вернул пустой список, используем локальную фильтрацию
            DialogResult = true;
            Close();
        }

        private async Task<List<CatalogProductTileVm>?> FetchFilteredProductsAsync(FilterCriteria criteria)
        {
            var queryParams = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(criteria.SearchQuery))
                queryParams["search"] = criteria.SearchQuery;
            if (criteria.OnlyWeight)
                queryParams["is_weight"] = "true";
            if (criteria.OnlyInStock)
                queryParams["in_stock"] = "true";
            if (criteria.OnlyFavorite)
                queryParams["is_favorite"] = "true";
            if (criteria.DateFrom.HasValue)
                queryParams["date_from"] = criteria.DateFrom.Value.ToString("yyyy-MM-dd");
            if (criteria.DateTo.HasValue)
                queryParams["date_to"] = criteria.DateTo.Value.ToString("yyyy-MM-dd");

            var allTiles = new List<CatalogProductTileVm>();
            int page = 1;
            bool hasNext = true;

            while (hasNext)
            {
                var pageParams = new Dictionary<string, string>(queryParams)
                {
                    ["page"] = page.ToString(CultureInfo.InvariantCulture)
                };

                var response = await _api.GetAsync("api/main/products/list/", pageParams, CancellationToken.None);

                if (response.ValueKind == JsonValueKind.Object &&
                    response.TryGetProperty("results", out var results) &&
                    results.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in results.EnumerateArray())
                    {
                        var tile = ProductCatalogMapper.TryTile(el, App.Settings.ApiBaseUrl);
                        if (tile != null) allTiles.Add(tile);
                    }

                    hasNext = response.TryGetProperty("next", out var next) &&
                              next.ValueKind == JsonValueKind.String &&
                              !string.IsNullOrEmpty(next.GetString());
                    if (hasNext) page++;
                }
                else
                {
                    hasNext = false;
                }
            }

            return allTiles;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            DateFromPicker.SelectedDate = null;
            DateToPicker.SelectedDate = null;
            CategoryCombo.SelectedIndex = -1;
            BrandCombo.SelectedIndex = -1;
            ClientCombo.SelectedIndex = -1;
            StatusCombo.SelectedIndex = -1;
            HotkeyGroupCombo.SelectedIndex = -1;
            WeightCheck.IsChecked = false;
            InStockCheck.IsChecked = false;
            FavoriteCheck.IsChecked = false;
            var prefs = UserPreferences.Instance;
            prefs.LastFilterDateFrom = null;
            prefs.LastFilterDateTo = null;
            prefs.LastFilterCategory = null;
            prefs.LastFilterBrand = null;
            prefs.LastFilterClient = null;
            prefs.LastFilterStatus = null;
            prefs.LastFilterHotkeyGroup = null;
            prefs.LastFilterOnlyWeight = false;
            prefs.LastFilterOnlyInStock = false;
            prefs.LastFilterOnlyFavorite = false;
            prefs.SaveToDisk();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}