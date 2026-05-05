using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NurMarketKassa.Models;
using System.Net;
using System.Windows.Media.Animation;
using NurMarketKassa.Views;
using NurMarketKassa.ViewModels;
using NurMarketKassa.Configuration;


namespace NurMarketKassa.Views;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int CatalogInitialRenderCount = 48;
    private const int CatalogRenderStep = 36;
    private const int SearchInitialRenderCount = 40;
    private const int SearchRenderStep = 24;
    private string _barcodeBuf = "";
    private long _barcodeLastTick;
    private const int BarcodeInterkeyMs = 220;
    private const int MinBarcodeLen = 4;
    private const int BarcodeMaxLen = 64;

    private static Brush ThemeBrush(string key, Brush fallback) =>
        Application.Current.TryFindResource(key) as Brush ?? fallback;

    private static readonly SolidColorBrush FallbackMuted = new(Color.FromRgb(0x9C, 0xA3, 0xAF));
    private static readonly SolidColorBrush FallbackOk = new(Color.FromRgb(0x34, 0xD3, 0x99));
    private static readonly SolidColorBrush FallbackWarn = new(Color.FromRgb(0xFB, 0xBF, 0x24));
    private static readonly SolidColorBrush FallbackShiftOpenBg = new(Color.FromRgb(0x14, 0x3D, 0x2C));
    private static readonly SolidColorBrush FallbackShiftOpenBorder = new(Color.FromRgb(0x16, 0x65, 0x34));
    private static readonly SolidColorBrush FallbackShiftWarnBg = new(Color.FromRgb(0x42, 0x27, 0x06));
    private static readonly SolidColorBrush FallbackShiftWarnBorder = new(Color.FromRgb(0xB4, 0x53, 0x09));
    private static readonly SolidColorBrush FallbackToastNeutralBg = new(Color.FromRgb(0x1E, 0x29, 0x3B));
    private static readonly SolidColorBrush FallbackToastWarnBg = new(Color.FromRgb(0xB4, 0x53, 0x09));

    private static Brush UiMuted => ThemeBrush("BrushUiStatusMuted", FallbackMuted);
    private static Brush UiOk => ThemeBrush("BrushUiStatusOk", FallbackOk);
    private static Brush UiWarn => ThemeBrush("BrushUiStatusWarn", FallbackWarn);

    public ObservableCollection<CartLineRow> CartLines { get; } = new();

    public ObservableCollection<MainWindow.WarehouseItemVm> WarehouseItems { get; } = new();
    public ObservableCollection<WarehousePreset> WarehousePresets { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<string> Brands { get; } = new();
    private readonly List<CatalogProductTileVm> _allTilesKg = new();
    private readonly List<CatalogProductTileVm> _allTilesPiece = new();
    private readonly List<CatalogProductTileVm> _allSearchTiles = new();
    private readonly ObservableCollection<CatalogProductTileVm> _tilesKg = new();
    private readonly ObservableCollection<CatalogProductTileVm> _tilesPiece = new();
    private readonly ObservableCollection<CatalogProductTileVm> _searchTiles = new();
    private readonly ProductThumbService _catalogThumbService = new();
    private ScaleReaderService? _scaleService;
    private DispatcherTimer? _searchDebounceTimer;
    private DispatcherTimer? _scaleUiTimer;
    private DispatcherTimer? _toastTimer;
    private FilterCriteria? _currentFilter; 
    private string _pendingSearchQuery = "";
    private int _visibleKgCount;
    private int _visiblePieceCount;
    private int _visibleSearchCount;
    private bool _isUiBusy;
    private bool _allowMainWindowClose;
    private bool _logoutNavigateScheduled;
    private readonly CancellationTokenSource _windowCts = new();
    private bool _catalogLoadBusy;
    private bool _isMenuOpen;
    private bool _toolsPanelVisible = true;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    private bool HasActiveCartLines => App.Cart.HasCart && App.Cart.CanRefresh && CartLines.Count > 0;

    public static readonly DependencyProperty ShiftOpenForSaleProperty = DependencyProperty.Register(
        nameof(ShiftOpenForSale),
        typeof(bool),
        typeof(MainWindow),
        new PropertyMetadata(false));

    /// <summary>Можно добавлять товары в чек (открыта смена на кассе).</summary>
    public bool ShiftOpenForSale
    {
        get => (bool)GetValue(ShiftOpenForSaleProperty);
        private set => SetValue(ShiftOpenForSaleProperty, value);
    }

    public MainWindow()
    {
        InitializeComponent();
        InjectLayoutMetricsDefaults();
        DataContext = this;

        CatalogItemsKg.ItemsSource = _tilesKg;
        CatalogItemsPiece.ItemsSource = _tilesPiece;
        CatalogSearchItems.ItemsSource = _searchTiles;
        UpdateThemeButtonIcon();

        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(App.Settings.Catalog.SearchDebounceMs, 120, 2000)),
        };
        _searchDebounceTimer.Tick += SearchDebounce_Tick;

        var api = App.Api;
        UserTitleText.Text = TryUserLabel(api.UserPayload);
        //BranchText.Text = FormatBranchLine(api.ActiveBranchId);
        App.OfflineSync.StateChanged += OfflineSync_StateChanged;
        RebindCartUi();
        UpdateShiftBanner();
        UpdateOfflineModeUi();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var ct = _windowCts.Token;
        try
        {
            await Task.WhenAll(LoadProfileHeaderAsync(ct), RefreshShiftStateAsync(ct)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await EnsureSaleSessionReadyAsync(silent: true, cancellationToken: ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ApplyFullscreenPreference();
        if (UserPreferences.Instance.ScaleEnabled)
        {
            _scaleService = new ScaleReaderService(UserPreferences.Instance.ToScaleSettings());
            _scaleService.Start();
            _scaleUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _scaleUiTimer.Tick += (_, _) => UpdateScaleStatusLine();
            _scaleUiTimer.Start();
        }
        UpdateScaleStatusLine();

        _ = LoadCatalogAsync(ct);

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (CatalogAreaBorder.ActualWidth > 10)
                    UpdateCatalogTileLayoutMetrics(CatalogAreaBorder.ActualWidth);
                if (CartListView.ActualWidth > 10)
                    UpdateCartLineLayoutMetrics(CartListView.ActualWidth);
            }));

        _ = ScheduleStartupUpdateCheckAsync();
    }

    private async Task LoadProfileHeaderAsync(CancellationToken ct)
    {
        ProfileStatusText.Text = "Загрузка профиля…";
        ProfileStatusText.Foreground = UiMuted;

        try
        {
            var profile = await App.Api.GetProfileAsync(ct).ConfigureAwait(true);
            if (profile.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                ProfileStatusText.Text = "Данные профиля не получены.";
                ProfileStatusText.Foreground = UiWarn;
                return;
            }

            App.Api.ApplyBranchFromProfile(profile);
            UserTitleText.Text = TryUserLabel(profile);
            //var branchId = App.Api.ActiveBranchId ?? TryBranchId(profile);
            //BranchText.Text = FormatBranchLine(branchId);
            //BranchText.ToolTip = string.IsNullOrEmpty(branchId) ? null : branchId;
            ProfileStatusText.Text = "Профиль загружен.";
            ProfileStatusText.Foreground = UiOk;
        }
        catch (TaskCanceledException)
        {
            ProfileStatusText.Text = "Превышено время ожидания.";
            ProfileStatusText.Foreground = UiWarn;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApiException ex)
        {
            ProfileStatusText.Text = ex.Message;
            ProfileStatusText.Foreground = UiWarn;
        }
        catch (HttpRequestException ex)
        {
            ProfileStatusText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
            ProfileStatusText.Foreground = UiWarn;
        }
    }

    private async Task ScheduleStartupUpdateCheckAsync()
    {
        try
        {
            await Task.Delay(4000).ConfigureAwait(true);
            await AppUpdateService.TryOfferUpdateOnStartupAsync(this, App.Settings.Updates).ConfigureAwait(true);
        }
        catch
        {
            /* не мешаем работе кассы */
        }
    }

    /// <summary>Начальные значения DynamicResource для плиток каталога и строк корзины.</summary>
    private void InjectLayoutMetricsDefaults()
    {
        void Put(string key, double v) => Resources[key] = v;
        Put("CatalogTileWidth", 108);
        Put("CatalogTileMinHeight", 118);
        Put("CatalogTitleFont", 11);
        Put("CatalogPriceFont", 10);
        Put("CatalogTitleMaxH", 36);
        Put("CartUiTitleFont", 10.5);
        Put("CartUiLineTotalFont", 10.5);
        Put("CartUiSubFont", 8);
        Put("CartUiSmallBtnFont", 8);
        Put("CartUiSmallBtnMinH", 22);
        Put("CartUiQtyBtnSize", 32);
        Put("CartUiQtyBtnFont", 12);
        Put("CartUiSumColMinW", 76);
        Resources["CartUiQtyBtnSpacing"] = new Thickness(0, 0, 14, 0);
    }

    private void CatalogArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 10)
            UpdateCatalogTileLayoutMetrics(e.NewSize.Width);
    }

    private void CartListView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 10)
            UpdateCartLineLayoutMetrics(e.NewSize.Width);
    }

    private void UpdateCatalogTileLayoutMetrics(double panelWidth)
    {
        var inner = Math.Max(panelWidth - 56, 120);
        var tileW = Math.Clamp(inner / 5.15, 90, 140);
        var r = tileW / 108.0;
        Resources["CatalogTileWidth"] = tileW;
        Resources["CatalogTileMinHeight"] = Math.Clamp(100 * r, 96, 138);
        Resources["CatalogTitleFont"] = Math.Clamp(11 * r, 9.5, 13);
        Resources["CatalogPriceFont"] = Math.Clamp(10 * r, 8.5, 12);
        Resources["CatalogTitleMaxH"] = Math.Clamp(40 * r, 30, 56);
    }

    private void UpdateCartLineLayoutMetrics(double listWidth)
    {
        var w = Math.Max(listWidth - 8, 100);
        var scale = Math.Clamp(w / 292.0, 0.72, 1.2);
        Resources["CartUiTitleFont"] = Math.Clamp(9.5 * scale, 8.5, 13);
        Resources["CartUiLineTotalFont"] = Math.Clamp(9.5 * scale, 8.5, 13);
        Resources["CartUiSubFont"] = Math.Clamp(7.5 * scale, 6.5, 10);
        Resources["CartUiSmallBtnFont"] = Math.Clamp(7.5 * scale, 6.5, 9.5);
        Resources["CartUiSmallBtnMinH"] = Math.Clamp(22 * scale, 18, 28);
        Resources["CartUiQtyBtnSize"] = Math.Clamp(32 * scale, 28, 44);
        Resources["CartUiQtyBtnFont"] = Math.Clamp(12 * scale, 10, 16);
        Resources["CartUiSumColMinW"] = Math.Clamp(76 * scale, 68, 100);
        var gap = Math.Clamp(14 * scale, 10, 24);
        Resources["CartUiQtyBtnSpacing"] = new Thickness(0, 0, gap, 0);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!App.ExitWithoutLoginRedirect && !_allowMainWindowClose)
        {
            e.Cancel = true;
            if (_logoutNavigateScheduled)
                return;
            _logoutNavigateScheduled = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    NavigateToLogin();
                }
                finally
                {
                    _logoutNavigateScheduled = false;
                }
            }), DispatcherPriority.Normal);
            return;
        }

        _searchDebounceTimer?.Stop();
        _scaleUiTimer?.Stop();
        _toastTimer?.Stop();
        try
        {
            _windowCts.Cancel();
        }
        catch
        {
            /* ignore */
        }

        _windowCts.Dispose();
        App.OfflineSync.StateChanged -= OfflineSync_StateChanged;
        _scaleService?.Dispose();
        _scaleService = null;
    }

    private void ShowKeyboard_Click(object sender, RoutedEventArgs e) => TouchKeyboard.ShowOnDemand();

    private void OfflineSync_StateChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateOfflineModeUi();
            UpdateDeferredCartUi();
        });
    }

    private void UpdateOfflineModeUi()
    {
        var sync = App.OfflineSync;
        NetworkModeText.Text = sync.StatusText;
        NetworkModeText.Foreground = sync.IsOnline
            ? (sync.IsSyncInProgress ? UiWarn : UiOk)
            : UiWarn;
    }

    private void UpdateScaleStatusLine()
    {
        var sp = UserPreferences.Instance;
        if (sp.ScaleEnabled)
        {
            var s = _scaleService?.Status ?? "—";
            var w = _scaleService?.LastWeight;
            var wtxt = w is > 0 ? $"{w.Value.ToString("0.###", CultureInfo.InvariantCulture)} кг" : "—";
            ScaleStatusText.Text =
                $"Весы {sp.ScaleComPort} @ {sp.ScaleBaudRate}: {s} · на весах: {wtxt}";
        }
        else
        {
            ScaleStatusText.Text = "Весы выключены. Включите в «Настройки кассы» и выберите COM-порт.";
        }
    }

    private async Task<bool> EnsureSaleSessionReadyAsync(
        string pendingMessage = "Открываем новый чек…",
        string successMessage = "Новый чек открыт. Можно добавлять товары.",
        bool silent = false,
        CancellationToken cancellationToken = default)
    {
        // Смену при добавлении товара проверяем отдельно (всегда с диалогом) — см. PickProductFromCatalogAsync / RunScanAsync.
        if (App.Cart.CanRefresh)
            return true;

        if (!await EnsureShiftReadyForOperationsAsync(silent, cancellationToken).ConfigureAwait(true))
            return false;

        if (!silent)
        {
            CartMessageText.Text = pendingMessage;
            CartMessageText.Foreground = UiMuted;
        }

        try
        {
            await TryStartNewSaleAsync(cancellationToken).ConfigureAwait(true);
            RebindCartUi();
            if (!silent)
            {
                CartMessageText.Text = successMessage;
                CartMessageText.Foreground = UiOk;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            if (!silent)
            {
                CartMessageText.Text = "Отменено.";
                CartMessageText.Foreground = UiMuted;
            }

            return false;
        }
        catch (ApiException ex)
        {
            if (!silent)
            {
                CartMessageText.Text = ex.Message;
                CartMessageText.Foreground = UiWarn;
            }

            return false;
        }
        catch (HttpRequestException ex)
        {
            if (!silent)
            {
                CartMessageText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
                CartMessageText.Foreground = UiWarn;
            }

            return false;
        }
    }

    private void UpdateCartStateUi()
    {
        CartItemsCountText.Text = CartLines.Count.ToString(CultureInfo.InvariantCulture);

        if (!App.Cart.HasCart || !App.Cart.CanRefresh)
        {
            CartStateText.Text = string.IsNullOrEmpty(App.ActiveShiftId)
                ? "Откройте смену. После этого новый чек будет открываться автоматически."
                : "Новый чек откроется автоматически. Можно сразу сканировать товары.";
            return;
        }

        var positions = CartLines.Count;
        var total = CartDisplayHelper.TotalDue(App.Cart.Root);
        CartStateText.Text = positions == 0
            ? "Чек открыт. Можно сканировать товары."
            : $"Активный чек: {positions} поз. · {CartDisplayHelper.FormatMoney(total)} сом к оплате.";
    }

    private void UpdateDeferredCartUi()
    {
        var count = DeferredCartsStore.Count();
        var pendingOffline = OfflinePendingSalesStore.PendingCount;
        var failedOffline = OfflinePendingSalesStore.FailedCount;
        var offlineQueue = pendingOffline + failedOffline;

        DeferredCountText.Text = count.ToString(CultureInfo.InvariantCulture);

        //if (OfflineSaleButton != null)
        //    OfflineSaleButton.Content = pendingOffline > 0
        //        ? $"Оффлайн ({pendingOffline})"
        //        : "Оффлайн";

        //if (OfflineQueueButton != null)
        //    OfflineQueueButton.Content = offlineQueue > 0
        //        ? $"Очередь ({offlineQueue})"
        //        : "Очередь";

        if (DeferCartButton != null)
            SetScanBusy(_isUiBusy);
    }

    // Переключение вида склада: карточки
    private void WarehouseCardsBtn_Checked(object sender, RoutedEventArgs e)
    {
        if (WarehouseCardsItems != null && WarehouseGrid != null)
        {
            WarehouseCardsItems.Visibility = Visibility.Visible;
            WarehouseGrid.Visibility = Visibility.Collapsed;
        }
    }

    // Переключение вида склада: таблица
    private void WarehouseTableBtn_Checked(object sender, RoutedEventArgs e)
    {
        if (WarehouseCardsItems != null && WarehouseGrid != null)
        {
            WarehouseCardsItems.Visibility = Visibility.Collapsed;
            WarehouseGrid.Visibility = Visibility.Visible;
        }
    }

    private void WarehouseSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = (WarehouseSearchBox.Text ?? "").Trim().ToLower();
        CollectionViewSource.GetDefaultView(WarehouseItems).Filter = item =>
        {
            if (item is not MainWindow.WarehouseItemVm w) return true;
            if (string.IsNullOrEmpty(filter)) return true;
            return (w.ProductName?.ToLower().Contains(filter) ?? false) ||
                   (w.Code?.ToLower().Contains(filter) ?? false) ||
                   (w.Article?.ToLower().Contains(filter) ?? false);
        };
    }

    private void WarehouseBack_Click(object sender, RoutedEventArgs e)
    {
        WarehousePanel.Visibility = Visibility.Collapsed;
        CatalogAreaBorder.Visibility = Visibility.Visible;
        RightPanelBorder.Visibility = Visibility.Visible;
        CatalogGridSplitter.Visibility = Visibility.Visible;
    }

    private void WarehouseCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MainWindow.WarehouseItemVm item)
        {
            var dlg = new ProductDetailWindow(new ProductDetailVm
            {
                Id = item.Id,
                ProductName = item.ProductName,
                Barcode = item.Code,
                Article = item.Article,
                Code = item.Code,
                Price = item.Price,
                PurchasePrice = item.Price * 0.7m,
                MarkupPercent = 0.30m,
                Category = "Основная",
                Country = "Кыргызстан",
                Group = "Продовольствие",
                Description = item.ProductName,
                CreatedAt = DateTime.Now,
                ExpiryDate = DateTime.Now.AddMonths(6)
            });
            dlg.ShowDialog();
        }
    }

    private async Task LoadWarehouseItemsAsync()
    {
        WarehouseItems.Clear();
        foreach (var product in CatalogCacheService.Products)
        {
            decimal price = 0;
            if (product.PriceLine != null)
            {
                var s = product.PriceLine.Replace(" сом", "").Replace(" ", "");
                decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out price);
            }
            WarehouseItems.Add(new MainWindow.WarehouseItemVm
            {
                Id = product.Id,
                ProductName = product.Title,
                Code = product.Barcode ?? "—",
                Article = product.Barcode ?? "—",
                Unit = product.Unit ?? (product.MustWeigh ? "кг" : "шт"),
                Price = price,
                Discount = 0,
                StockQuantity = product.Quantity,
                StockBrush = product.Quantity <= 0
                    ? new SolidColorBrush(Color.FromRgb(255, 204, 204))
                    : Brushes.White
            });
        }

        try
        {
            var agentProducts = await App.Api.GetAgentProductsAsync(CancellationToken.None);
            if (agentProducts.Count > 0)
            {
                var qtyMap = new Dictionary<string, double>();
                foreach (var el in agentProducts)
                {
                    if (el.TryGetProperty("id", out var idEl))
                    {
                        string id = idEl.ValueKind == JsonValueKind.Number
                            ? idEl.GetInt32().ToString()
                            : (idEl.GetString() ?? "");
                        if (!string.IsNullOrEmpty(id))
                        {
                            double qty = 0;
                            if (el.TryGetProperty("quantity", out var qEl))
                            {
                                if (qEl.ValueKind == JsonValueKind.Number)
                                    qty = qEl.GetDouble();
                                else if (qEl.ValueKind == JsonValueKind.String)
                                    double.TryParse(qEl.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out qty);
                            }
                            qtyMap[id] = qty;
                        }
                    }
                }

                foreach (var item in WarehouseItems)
                {
                    if (qtyMap.TryGetValue(item.Id, out double qty))
                    {
                        item.StockQuantity = qty;
                        item.StockBrush = qty <= 0
                            ? new SolidColorBrush(Color.FromRgb(255, 204, 204))
                            : Brushes.White;
                    }
                }
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
        catch { /* ignore */ }
    }

    private void LoadWarehouseItems()
    {
        WarehouseItems.Clear();
        foreach (var product in CatalogCacheService.Products)
        {
            decimal price = 0;
            if (product.PriceLine != null)
            {
                var s = product.PriceLine.Replace(" сом", "").Replace(" ", "");
                decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out price);
            }
            WarehouseItems.Add(new MainWindow.WarehouseItemVm
            {
                Id = product.Id,
                ProductName = product.Title,
                Code = product.Barcode ?? "—",
                Article = product.Barcode ?? "—",
                Unit = product.Unit ?? (product.MustWeigh ? "кг" : "шт"),
                Price = price,
                Discount = 0,
                StockQuantity = product.Quantity,
                StockBrush = product.Quantity <= 0
                    ? new SolidColorBrush(Color.FromRgb(255, 204, 204))
                    : Brushes.White
            });
        }
    }

    private string BuildDeferredCartLabel()
    {
        var items = CartDisplayHelper.EnumerateItems(App.Cart.Root).ToList();
        var total = CartDisplayHelper.TotalDue(App.Cart.Root);
        if (items.Count == 0)
            return $"Чек {DateTime.Now:dd.MM HH:mm}";

        var firstTitle = TrimCaption(CartDisplayHelper.ItemName(items[0]), 22);
        var suffix = items.Count > 1 ? $" +{items.Count - 1}" : "";
        return $"{DateTime.Now:HH:mm} · {firstTitle}{suffix} · {CartDisplayHelper.FormatMoney(total)} сом";
    }

    private static string TrimCaption(string? value, int maxLen)
    {
        var text = (value ?? "").Trim();
        if (text.Length <= maxLen)
            return text;
        return text.Substring(0, Math.Max(0, maxLen - 1)).TrimEnd() + "…";
    }

    private DeferredCartEntry SaveCurrentCartAsDeferred(string? label = null)
    {
        var entry = new DeferredCartEntry
        {
            Label = string.IsNullOrWhiteSpace(label) ? BuildDeferredCartLabel() : label.Trim(),
            CartJson = App.Cart.Root.GetRawText(),
        };
        DeferredCartsStore.Add(entry);
        UpdateDeferredCartUi();
        try
        {
            PosLogger.Log($"DEFER save: id={entry.Id}, label={entry.Label}, count={DeferredCartsStore.Count()}", "DEFER");
        }
        catch
        {
            /* ignore */
        }
        return entry;
    }

    private async Task<bool> DeferCurrentCartAsync(bool startNewSale = true, bool showToast = true, string? label = null)
    {
        if (!HasActiveCartLines)
        {
            if (showToast)
                ShowToast("Корзина пуста — нечего откладывать.", warn: true);
            return false;
        }

        if (startNewSale && !await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return false;

        var entry = SaveCurrentCartAsDeferred(label);
        if (showToast)
            ShowToast($"Отложено: «{entry.Label}».");

        if (startNewSale)
            await ClearCartAfterDeferAsync().ConfigureAwait(true);

        return true;
    }

    private async Task LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (_catalogLoadBusy)
            return;

        _catalogLoadBusy = true;
        if (RefreshCatalogButton != null)
            RefreshCatalogButton.IsEnabled = false;
        //var prevStats = CatalogStatsText.Text;
        //CatalogStatsText.Text = "Каталог: загрузка…";

        try
        {
            try
            {
                await Task.Delay(120, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var raw = await App.Api
                .ProductsCatalogAsync(App.Settings.Catalog.QuickCatalogLimit, App.Settings.Catalog.CatalogMaxPages, cancellationToken)
                .ConfigureAwait(true);
            var apiBase = App.Settings.ApiBaseUrl;
            _allTilesKg.Clear();
            _allTilesPiece.Clear();
            _tilesKg.Clear();
            _tilesPiece.Clear();
            foreach (var el in raw)
            {
                var vm = ProductCatalogMapper.TryTile(el, apiBase);
                if (vm == null)
                    continue;
                if (vm.MustWeigh)
                    _allTilesKg.Add(vm);
                else
                    _allTilesPiece.Add(vm);
            }

            ShowToast($"Каталог: {_allTilesKg.Count} весовых, {_allTilesPiece.Count} штучных.");
            ResetCatalogViewport();
        }
        catch (TaskCanceledException)
        {
            ShowToast("Каталог: таймаут.", warn: true);
        }
        catch (OperationCanceledException)
        {
            //CatalogStatsText.Text = prevStats;
        }
        catch (ApiException ex)
        {
            ShowToast($"Каталог: {ex.Message}", warn: true);
        }
        catch (HttpRequestException ex)
        {
            ShowToast(string.IsNullOrWhiteSpace(ex.Message) ? "Каталог: нет сети." : $"Каталог: {ex.Message}", warn: true);
        }
        finally
        {
            _catalogLoadBusy = false;
            if (RefreshCatalogButton != null)
                RefreshCatalogButton.IsEnabled = true;
            UpdateCatalogPagerUi();
        }
    }

    private void ResetCatalogViewport()
    {
        _visibleKgCount = 0;
        _visiblePieceCount = 0;
        EnsureActiveCatalogPageVisible();
        ApplyCatalogViewport();
    }

    private void EnsureActiveCatalogPageVisible()
    {
        if (GetSelectedCatalogTabIndex() == 1)
            _visiblePieceCount = Math.Min(_visiblePieceCount == 0 ? CatalogInitialRenderCount : _visiblePieceCount, _allTilesPiece.Count);
        else
            _visibleKgCount = Math.Min(_visibleKgCount == 0 ? CatalogInitialRenderCount : _visibleKgCount, _allTilesKg.Count);
    }

    private int GetSelectedCatalogTabIndex() =>
        CatalogTabs?.SelectedIndex is 1 ? 1 : 0;

    private void ApplyCatalogViewport()
    {
        var selectedTab = GetSelectedCatalogTabIndex();
        SyncVisibleTiles(_tilesKg, selectedTab == 0 ? _allTilesKg : [], _visibleKgCount);
        SyncVisibleTiles(_tilesPiece, selectedTab == 1 ? _allTilesPiece : [], _visiblePieceCount);
        UpdateCatalogPagerUi();
        WarmVisibleWeighedThumbs();
    }

    private void SyncVisibleTiles(
        ObservableCollection<CatalogProductTileVm> target,
        IReadOnlyList<CatalogProductTileVm> source,
        int visibleCount)
    {
        target.Clear();
        foreach (var vm in source.Take(Math.Min(visibleCount, source.Count)))
            target.Add(vm);
    }

    private void UpdateCatalogPagerUi()
    {
        // Временно отключено – требуется XAML
    }

    private void CatalogMore_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedCatalogTabIndex() == 1)
            _visiblePieceCount = Math.Min(_visiblePieceCount + CatalogRenderStep, _allTilesPiece.Count);
        else
            _visibleKgCount = Math.Min(_visibleKgCount + CatalogRenderStep, _allTilesKg.Count);

        ApplyCatalogViewport();
    }

    private void CatalogSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _pendingSearchQuery = (CatalogSearchBox.Text ?? "").Trim();
        _searchDebounceTimer?.Stop();
        if (_pendingSearchQuery.Length < 2)
        {
            _allSearchTiles.Clear();
            _searchTiles.Clear();
            _visibleSearchCount = 0;
            SearchOverlayPanel.Visibility = Visibility.Collapsed;
            return;
        }

        _searchDebounceTimer?.Start();
    }

    private async void CatalogGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CatalogGrid.SelectedItem is CatalogProductTileVm vm)
            await PickProductFromCatalogAsync(vm);
    }

    private void ToggleToolsPanel_Click(object sender, RoutedEventArgs e)
    {
        _toolsPanelVisible = !_toolsPanelVisible;
        ToolsPanel.Visibility = _toolsPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        UserPreferences.Instance.ToolsPanelExpanded = _toolsPanelVisible;
        UserPreferences.Instance.SaveToDisk();
        ToggleToolsButton.Content = _toolsPanelVisible ? "∨" : "∧";
    }



    private async void SearchDebounce_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer?.Stop();
        var q = _pendingSearchQuery;
        if (q.Length < 2)
            return;

        try
        {
            var items = await App.Api
                .ProductsSearchAsync(q, App.Settings.Catalog.SearchLimit, _windowCts.Token)
                .ConfigureAwait(true);
            var apiBase = App.Settings.ApiBaseUrl;
            _allSearchTiles.Clear();
            _searchTiles.Clear();
            foreach (var el in items)
            {
                var vm = ProductCatalogMapper.TryTile(el, apiBase);
                if (vm != null)
                    _allSearchTiles.Add(vm);
            }

            _visibleSearchCount = Math.Min(SearchInitialRenderCount, _allSearchTiles.Count);
            ApplySearchViewport();
            SearchOverlayPanel.Visibility = _searchTiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiException ex)
        {
            ShowToast($"Поиск: {ex.Message}", warn: true);
        }
        catch (HttpRequestException ex)
        {
            ShowToast(string.IsNullOrWhiteSpace(ex.Message) ? "Поиск: нет сети." : ex.Message, warn: true);
        }
    }

    private async void RefreshCatalog_Click(object sender, RoutedEventArgs e) =>
        await LoadCatalogAsync(_windowCts.Token).ConfigureAwait(true);

    private async void ReturnSale_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return;
        var dlg = new ReturnSaleDialog { Owner = this };
        dlg.ShowDialog();
    }

    private void CatalogTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != CatalogTabs)
            return;

        EnsureActiveCatalogPageVisible();
        ApplyCatalogViewport();
    }

    private void ApplySearchViewport()
    {
        SyncVisibleTiles(_searchTiles, _allSearchTiles, _visibleSearchCount);
        if (SearchOverlayTitle != null)
            SearchOverlayTitle.Text = _allSearchTiles.Count == 0
                ? "Поиск"
                : $"Поиск «{_pendingSearchQuery}» — {_searchTiles.Count}/{_allSearchTiles.Count}";
        if (SearchMoreButton != null)
            SearchMoreButton.Visibility = _searchTiles.Count < _allSearchTiles.Count ? Visibility.Visible : Visibility.Collapsed;
        WarmVisibleWeighedThumbs();
    }

    private void WarmVisibleWeighedThumbs()
    {
        _ = WarmVisibleWeighedThumbsAsync(_windowCts.Token);
    }

    private async Task WarmVisibleWeighedThumbsAsync(CancellationToken cancellationToken)
    {
        var apiBase = App.Settings.ApiBaseUrl;
        var visible = _tilesKg
            .Concat(_searchTiles)
            .Where(vm => vm.MustWeigh && vm.Thumb == null && !string.IsNullOrWhiteSpace(vm.ImageUrl))
            .ToList();

        foreach (var vm in visible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _catalogThumbService
                    .SetThumbAsync(Dispatcher, App.Api, apiBase, vm.ImageUrl!, vm, cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                /* ignore thumb failures on catalog tiles */
            }
        }
    }

    private void SearchMore_Click(object sender, RoutedEventArgs e)
    {
        _visibleSearchCount = Math.Min(_visibleSearchCount + SearchRenderStep, _allSearchTiles.Count);
        ApplySearchViewport();
    }

    private void CatalogProductDetail_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement fe || fe.DataContext is not CatalogProductTileVm vm)
            return;
        var detailVm = new ProductDetailVm
        {
            Id = vm.Id,
            ProductName = vm.Title,
            Barcode = vm.Barcode ?? "",
            Article = vm.Barcode ?? "",
            Code = vm.Barcode ?? "",
            Category = vm.Category ?? "Основная",
            Country = "Кыргызстан",
            Group = "Продовольственные",
            Description = vm.Title,
            Price = decimal.TryParse(vm.PriceLine?.Replace(" сом", ""), out var p) ? p : 0m,
            PurchasePrice = 0m,
            MarkupPercent = 0m,
            CreatedAt = DateTime.Now,
            ExpiryDate = DateTime.Now.AddMonths(6)
        };
        var dlg = new ProductDetailWindow(detailVm) { Owner = this };
        dlg.ShowDialog();
    }

    private async void CatalogProduct_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not CatalogProductTileVm vm)
            return;
        await PickProductFromCatalogAsync(vm).ConfigureAwait(true);
    }

    private async Task PickProductFromCatalogAsync(CatalogProductTileVm vm)
    {
        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return;
        if (!await EnsureSaleSessionReadyAsync().ConfigureAwait(true))
            return;

        string? qty = null;
        if (vm.MustWeigh)
        {
            var dlg = new WeighedProductDialog(vm.Title, vm.PriceLine, _scaleService) { Owner = this };
            if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.QuantityNormalized))
                return;
            qty = dlg.QuantityNormalized;
        }

        SetScanBusy(true);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var resp = await App.Api.PosAddItemAsync(App.Cart.CartId!, vm.Id, qty).ConfigureAwait(true);
                    if (!CartResponseHelper.TryUpdateCartSession(resp, App.Cart))
                        await ReloadCartFromServerAsync().ConfigureAwait(true);
                    if (vm.MustWeigh)
                        CartDisplayHelper.HintProductWeighedForDisplay(vm.Id);
                    RebindCartUi();
                    CartMessageText.Text = "Товар добавлен.";
                    CartMessageText.Foreground = UiOk;
                    CatalogSearchBox.Text = "";
                    SearchOverlayPanel.Visibility = Visibility.Collapsed;
                    ShowToast(vm.MustWeigh ? $"Добавлено {qty} кг" : "Товар добавлен в чек");
                    return;
                }
                catch (ApiException ex) when (attempt == 0 && CartResponseHelper.LooksLikeStaleCart(ex))
                {
                    try
                    {
                        await TryStartNewSaleAsync().ConfigureAwait(true);
                        RebindCartUi();
                        ShowToast("Корзина устарела — новая продажа, повторяем добавление.", warn: true);
                    }
                    catch (ApiException rex)
                    {
                        var ru = PosErrorMessages.UserMessageForCatalogOrScan(rex);
                        CartMessageText.Text = ru;
                        CartMessageText.Foreground = UiWarn;
                        ShowToast(ru, warn: true);
                        return;
                    }
                }
            }

            ShowToast("Не удалось добавить товар после повтора.", warn: true);
        }
        catch (ApiException ex)
        {
            var ru = PosErrorMessages.UserMessageForCatalogOrScan(ex);
            CartMessageText.Text = ru;
            CartMessageText.Foreground = UiWarn;
            ShowToast(ru, warn: true);
        }
        catch (HttpRequestException ex)
        {
            var m = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
            CartMessageText.Text = m;
            CartMessageText.Foreground = UiWarn;
            ShowToast(m, warn: true);
        }
        catch (TaskCanceledException)
        {
            ShowToast("Таймаут запроса.", warn: true);
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    // ────────── Гамбургер-меню ──────────
    private void HamburgerMenu_Click(object sender, RoutedEventArgs e)
    {
        _isMenuOpen = !_isMenuOpen;
        AnimateHamburgerMenu(_isMenuOpen);
    }

    private void HamburgerMenuClose_Click(object sender, RoutedEventArgs e)
    {
        _isMenuOpen = false;
        AnimateHamburgerMenu(false);
    }

    private void HamburgerOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isMenuOpen)
        {
            _isMenuOpen = false;
            AnimateHamburgerMenu(false);
        }
    }

    private void AnimateHamburgerMenu(bool open)
    {
        double from = open ? -320 : 0;
        double to = open ? 0 : -320;

        var animation = new ThicknessAnimation
        {
            From = new Thickness(from, 0, 0, 0),
            To = new Thickness(to, 0, 0, 0),
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new QuadraticEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn }
        };

        if (open) HamburgerOverlay.Visibility = Visibility.Visible;

        animation.Completed += (s, args) =>
        {
            if (!open) HamburgerOverlay.Visibility = Visibility.Collapsed;
        };

        HamburgerPanel.BeginAnimation(MarginProperty, animation);
    }

    internal void ShowToast(string message, bool warn = false)
    {
        ToastText.Text = message;
        ToastPanel.Background = warn
            ? ThemeBrush("BrushToastWarnBg", FallbackToastWarnBg)
            : ThemeBrush("BrushToastNeutralBg", FallbackToastNeutralBg);
        ToastPanel.BorderBrush = warn
            ? ThemeBrush("BrushShiftWarnBorder", FallbackShiftWarnBorder)
            : ThemeBrush("BrushBorderStrong", new SolidColorBrush(Color.FromRgb(0x51, 0x65, 0x85)));
        ToastPanel.Visibility = Visibility.Visible;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastPanel.Visibility = Visibility.Collapsed;
        };
        _toastTimer.Start();
    }

    private async void ApplyOrderDiscount_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Cart.CanRefresh)
        {
            ShowToast("Нет активной корзины.", warn: true);
            return;
        }

        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return;

        var dlg = new OrderDiscountDialog(GetCurrentOrderDiscountPercent(), GetCurrentOrderDiscountSum())
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true)
            return;

        if (dlg.ClearRequested)
        {
            await ClearOrderDiscountAsync().ConfigureAwait(true);
            return;
        }

        var body = dlg.DiscountMode == "percent"
            ? new Dictionary<string, string> { ["order_discount_percent"] = dlg.DiscountValue }
            : new Dictionary<string, string> { ["order_discount_total"] = dlg.DiscountValue };

        await ApplyOrderDiscountBodyAsync(body, "Скидка на чек обновлена.").ConfigureAwait(true);
    }

    private async void ClearOrderDiscount_Click(object sender, RoutedEventArgs e)
    {
        await ClearOrderDiscountAsync().ConfigureAwait(true);
    }

    private async Task ApplyOrderDiscountBodyAsync(Dictionary<string, string> body, string successMessage)
    {
        SetScanBusy(true);
        try
        {
            var resp = await App.Api.PosCartPatchAsync(App.Cart.CartId!, body).ConfigureAwait(true);
            if (!CartResponseHelper.TryUpdateCartSession(resp, App.Cart))
                await ReloadCartFromServerAsync().ConfigureAwait(true);
            RebindCartUi();
            ShowToast(successMessage);
        }
        catch (ApiException ex)
        {
            ShowToast(ex.Message, warn: true);
        }
        catch (HttpRequestException ex)
        {
            ShowToast(string.IsNullOrWhiteSpace(ex.Message) ? "Нет сети." : ex.Message, warn: true);
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private async Task ClearOrderDiscountAsync()
    {
        if (!App.Cart.CanRefresh)
            return;

        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return;

        SetScanBusy(true);
        try
        {
            try
            {
                var resp = await App.Api
                    .PosCartPatchAsync(
                        App.Cart.CartId!,
                        new Dictionary<string, string>
                        {
                            ["order_discount_percent"] = "0",
                            ["order_discount_total"] = "0",
                        })
                    .ConfigureAwait(true);
                if (!CartResponseHelper.TryUpdateCartSession(resp, App.Cart))
                    await ReloadCartFromServerAsync().ConfigureAwait(true);
            }
            catch (ApiException)
            {
                var resp2 = await App.Api
                    .PosCartPatchAsync(App.Cart.CartId!, new Dictionary<string, string> { ["order_discount_percent"] = "0" })
                    .ConfigureAwait(true);
                if (!CartResponseHelper.TryUpdateCartSession(resp2, App.Cart))
                    await ReloadCartFromServerAsync().ConfigureAwait(true);
            }

            RebindCartUi();
            ShowToast("Скидка на чек сброшена.");
        }
        catch (ApiException ex)
        {
            ShowToast(ex.Message, warn: true);
        }
        catch (HttpRequestException ex)
        {
            ShowToast(string.IsNullOrWhiteSpace(ex.Message) ? "Нет сети." : ex.Message, warn: true);
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private void MenuMain_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        CatalogAreaBorder.Visibility = Visibility.Visible;
        RightPanelBorder.Visibility = Visibility.Visible;
        CatalogGridSplitter.Visibility = Visibility.Visible;
        WarehousePanel.Visibility = Visibility.Collapsed;
        ShiftPanel.Visibility = Visibility.Collapsed;
        SearchOverlayPanel.Visibility = Visibility.Collapsed;
    }

    private void MenuShift_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        CatalogAreaBorder.Visibility = Visibility.Collapsed;
        RightPanelBorder.Visibility = Visibility.Collapsed;
        CatalogGridSplitter.Visibility = Visibility.Collapsed;
        WarehousePanel.Visibility = Visibility.Collapsed;
        ShiftPanel.Visibility = Visibility.Visible;
    }

    private void ShiftBack_Click(object sender, RoutedEventArgs e)
    {
        ShiftPanel.Visibility = Visibility.Collapsed;
        CatalogAreaBorder.Visibility = Visibility.Visible;
        RightPanelBorder.Visibility = Visibility.Visible;
        CatalogGridSplitter.Visibility = Visibility.Visible;
    }

    private async void MenuWarehouse_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        await LoadWarehouseItemsAsync();
        WarehousePanel.Visibility = Visibility.Visible;
        CatalogAreaBorder.Visibility = Visibility.Collapsed;
        RightPanelBorder.Visibility = Visibility.Collapsed;
        CatalogGridSplitter.Visibility = Visibility.Collapsed;
        SearchOverlayPanel.Visibility = Visibility.Collapsed;
    }

    private void MenuServices_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        MessageBox.Show("Переход в раздел: Доп услуги", "Меню", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenFinanceWindow_Click(object sender, RoutedEventArgs e)
    {
        var finance = new FinanceWindow { Owner = this };
        finance.ShowDialog();
    }

    private void CloseMenu()
    {
        if (_isMenuOpen)
        {
            _isMenuOpen = false;
            AnimateHamburgerMenu(false);
        }
    }

    private void PrintSelfCheck_Click(object sender, RoutedEventArgs e)
    {
        var cfg = UserPreferences.Instance.ToReceiptPrinterSettings();
        if (string.IsNullOrWhiteSpace(cfg.DevicePath))
        {
            MessageBox.Show(
                "Укажите LPT-порт в «Настройки кассы».",
                "Принтер",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!cfg.Enabled)
        {
            MessageBox.Show(
                "Печать выключена в «Настройки кассы». Включите и укажите LPT.",
                "Принтер",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            EscPosTextReceiptPrinter.ValidateSettings(cfg);
            EscPosSelfCheckPrinter.PrintSelfCheck(cfg);
            ShowToast("Тестовая страница отправлена на принтер.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Принтер: ошибка.\n\n" + ex.Message + "\n\nПроверьте LPT в настройках и кабель.",
                "Печать",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ScaleTest_Click(object sender, RoutedEventArgs e)
    {
        var sp = UserPreferences.Instance;
        if (!sp.ScaleEnabled)
        {
            MessageBox.Show(
                "Весы выключены. Включите в «Настройки кассы» и укажите COM-порт.",
                "Весы",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            ScaleReaderService.ValidateSettings(sp.ToScaleSettings());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Весы", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var st = _scaleService?.Status ?? "нет сервиса";
        var w = _scaleService?.LastWeight;
        MessageBox.Show(
            ScaleReaderService.BuildStatusSummary(sp.ToScaleSettings(), st, w) +
            "\n\nПоложите товар на платформу и повторите тест.",
            "Тест весов",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        ShowToast("Синхронизация запущена…", false);
        try
        {
            await App.OfflineSync.TriggerSyncNowAsync(CancellationToken.None);
            int failed = OfflinePendingSalesStore.FailedCount;
            int pending = OfflinePendingSalesStore.PendingCount;
            if (failed == 0 && pending == 0)
                ShowToast("Все чеки синхронизированы.", false);
            else if (failed > 0)
                ShowToast($"Ошибок синхронизации: {failed}. Система повторит автоматически.", true);
            else
                ShowToast($"Ожидают отправки: {pending}. Попробуйте позже.", false);
        }
        catch (Exception ex)
        {
            ShowToast("Ошибка синхронизации: " + ex.Message, true);
        }
        finally
        {
            UpdateOfflineModeUi();
            UpdateDeferredCartUi();
        }
    }

    private void OfflineQueueInfo_Click(object sender, RoutedEventArgs e)
    {
        var n = OfflinePendingSalesStore.PendingCount;
        var failed = OfflinePendingSalesStore.FailedCount;
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NurMarketKassa",
            "offline_sales_pending.json");
        MessageBox.Show(
            $"Ожидают синхронизации: {n}\nОшибок синхронизации: {failed}\n\nФайл данных:\n{path}\n\nСинхронизация выполняется автоматически при восстановлении сети.",
            "Оффлайн-продажи",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void OfflineCheckout_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await OfflineCheckout_ClickCoreAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Оффлайн оплата", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OfflineCheckout_ClickCoreAsync()
    {
        if (App.Api is null)
        {
            MessageBox.Show("Подключение не готово.", "Оффлайн оплата", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!App.Cart.HasCart || !App.Cart.CanRefresh || CartLines.Count == 0)
        {
            MessageBox.Show("Добавьте товары в корзину.", "Оффлайн оплата", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var total = CartDisplayHelper.TotalDue(App.Cart.Root);
        var dlg = new CheckoutDialog(total) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        var paymentMethod = dlg.PaymentMethodKey;
        var wantPrintReceipt = dlg.WantPrintReceipt;
        var cashReceived = dlg.CashReceivedForApi;
        var saved = SaveCurrentSaleOffline(paymentMethod, cashReceived);

        var cfg = UserPreferences.Instance.ToReceiptPrinterSettings();
        if (wantPrintReceipt && UserPreferences.Instance.ReceiptEnabled && cfg.Enabled &&
            !string.IsNullOrWhiteSpace(cfg.DevicePath))
        {
            try
            {
                var txt = CartReceiptTextBuilder.BuildSimpleReceipt(
                    saved.CartJson,
                    "ОФФЛАЙН (ожидает выгрузку)",
                    saved.PaymentMethod,
                    saved.CashReceived);
                EscPosTextReceiptPrinter.Print(cfg, txt);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Чек не напечатан: " + ex.Message, "Печать", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        var saleRestartErr = await TryRestartSaleSessionAfterCheckoutAsync().ConfigureAwait(true);
        if (saleRestartErr != null)
        {
            CartMessageText.Text = "Оффлайн чек сохранён. " + saleRestartErr;
            CartMessageText.Foreground = UiWarn;
        }
        else
        {
            CartMessageText.Text = "Оффлайн чек сохранён. Новый чек открыт — добавьте товары.";
            CartMessageText.Foreground = UiOk;
        }

        MessageBox.Show(
            "Оплата сохранена локально.\n\n" +
            $"В очереди сейчас: {OfflinePendingSalesStore.PendingCount} чек(ов).",
            "Оффлайн оплата",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private OfflineSaleEntry SaveCurrentSaleOffline(string? paymentMethod, string? cashReceived)
    {
        var entry = new OfflineSaleEntry
        {
            PaymentMethod = paymentMethod ?? "",
            CashReceived = cashReceived,
            CartJson = App.Cart.Root.GetRawText(),
            CartId = App.Cart.CartId,
            ShiftId = App.ActiveShiftId,
            BranchId = App.Api.ActiveBranchId,
            CashboxId = App.PosCashboxId,
        };
        OfflinePendingSalesStore.Append(entry);
        UpdateOfflineModeUi();
        return entry;
    }

    private async void DeferCart_Click(object sender, RoutedEventArgs e)
    {
        await DeferCurrentCartAsync().ConfigureAwait(true);
    }

    private async Task ClearCartAfterDeferAsync()
    {
        SetScanBusy(true);
        try
        {
            // UX: сразу убрать текущий чек с экрана после "Отложить",
            // даже если открытие нового чека на API не получится.
            App.Cart.Clear();
            RebindCartUi();

            // Пробуем автоматически открыть новый чек. Некоторые API могут вернуть тот же чек/непустую корзину.
            // В таком случае оставляем экран очищенным и просим начать новый чек вручную.
            try
            {
                await TryStartNewSaleAsync().ConfigureAwait(true);
                RebindCartUi();
                if (CartLines.Count > 0)
                {
                    App.Cart.Clear();
                    RebindCartUi();
                    CartMessageText.Text = "Чек отложен. Нажмите «Новый», чтобы открыть новый чек.";
                    CartMessageText.Foreground = UiWarn;
                }
                else
                {
                    CartMessageText.Text = "Новый чек открыт — добавьте товары.";
                    CartMessageText.Foreground = UiOk;
                }
            }
            catch
            {
                CartMessageText.Text = "Чек отложен. Нажмите «Новый», чтобы открыть новый чек.";
                CartMessageText.Foreground = UiWarn;
            }
        }
        catch (Exception ex)
        {
            CartMessageText.Text = "Отложено. Начните продажу вручную: " + ex.Message;
            CartMessageText.Foreground = UiWarn;
            App.Cart.Clear();
            RebindCartUi();
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private async void OpenDeferredCarts_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new DeferredCartsDialog { Owner = this };
        var result = dlg.ShowDialog();
        UpdateDeferredCartUi();
        if (result != true || dlg.EntriesToRestore.Count == 0)
            return;

        SetScanBusy(true);
        try
        {
            await RestoreDeferredCartsAsync(
                    dlg.EntriesToRestore.OrderBy(x => x.SavedAt).ToList(),
                    dlg.RestoreMode)
                .ConfigureAwait(true);
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private async void RestoreLatestDeferred_Click(object sender, RoutedEventArgs e)
    {
        var latest = DeferredCartsStore.TryGetLatest();
        if (latest == null)
        {
            UpdateDeferredCartUi();
            ShowToast("Нет отложенных чеков.", warn: true);
            return;
        }

        SetScanBusy(true);
        try
        {
            ShowToast($"Возвращаем: «{latest.Label}»…");
            try
            {
                PosLogger.Log($"DEFER restore-latest: id={latest.Id}, label={latest.Label}", "DEFER");
            }
            catch
            {
                /* ignore */
            }
            await RestoreDeferredCartsAsync([latest], DeferredRestoreMode.ReplaceCurrentCart).ConfigureAwait(true);
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private async Task RestoreDeferredCartsAsync(
        IReadOnlyList<DeferredCartEntry> entries,
        DeferredRestoreMode restoreMode)
    {
        if (App.Api is null || entries.Count == 0)
            return;

        if (restoreMode == DeferredRestoreMode.ReplaceCurrentCart)
        {
            if (HasActiveCartLines)
            {
                var answer = MessageBox.Show(
                    this,
                    "В текущем чеке уже есть товары.\n\nОтложить текущий чек и открыть выбранный отдельно?",
                    "Отложенные чеки",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                    return;

                var deferred = await DeferCurrentCartAsync(startNewSale: false, showToast: false).ConfigureAwait(true);
                if (!deferred)
                    return;
            }

            if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
                return;

            try
            {
                await TryStartNewSaleAsync().ConfigureAwait(true);
                RebindCartUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось открыть новую продажу: " + ex.Message,
                    "Отложенные",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }
        else if (!App.Cart.HasCart || !App.Cart.CanRefresh)
        {
            if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
                return;

            try
            {
                await TryStartNewSaleAsync().ConfigureAwait(true);
                RebindCartUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось открыть новую продажу: " + ex.Message,
                    "Отложенные",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        try
        {
            foreach (var hold in entries)
            {
                var restored = false;
                for (var attempt = 0; attempt < 2 && !restored; attempt++)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(
                            string.IsNullOrWhiteSpace(hold.CartJson) ? "{}" : hold.CartJson);
                        foreach (var it in CartDisplayHelper.EnumerateItems(doc.RootElement))
                        {
                            var pid = CartDisplayHelper.TryProductId(it);
                            if (string.IsNullOrEmpty(pid))
                                continue;
                            var weighed = CartDisplayHelper.LineMustWeigh(it);
                            var qty = CartDisplayHelper.LineQuantity(it);
                            var qtyStr = FormatQuantityForApi(qty, weighed);
                            var up = CartDisplayHelper.UnitPrice(it);
                            var upStr = CartDisplayHelper.FormatMoney(up);
                            var disc = CartDisplayHelper.OptionalDiscountTotalParam(it);
                            var resp = await App.Api
                                .PosAddItemAsync(App.Cart.CartId!, pid, qtyStr, upStr, disc)
                                .ConfigureAwait(true);
                            if (!CartResponseHelper.TryUpdateCartSession(resp, App.Cart))
                                await ReloadCartFromServerAsync().ConfigureAwait(true);
                        }

                        restored = true;
                    }
                    catch (ApiException) when (attempt == 0 && restoreMode == DeferredRestoreMode.ReplaceCurrentCart)
                    {
                        // Если восстановление "как отдельный чек" сорвалось на середине,
                        // проще откатиться на новый пустой чек и попробовать 1 повтор,
                        // чтобы не получить смешанный/частичный чек.
                        try
                        {
                            await TryStartNewSaleAsync().ConfigureAwait(true);
                            RebindCartUi();
                        }
                        catch
                        {
                            throw;
                        }
                    }
                }

                // Удаляем сразу после успешного восстановления, чтобы повторное открытие
                // не приводило к дублям при частичных сбоях/перезапуске приложения.
                DeferredCartsStore.RemoveIds([hold.Id]);
            }

            RebindCartUi();
            UpdateDeferredCartUi();
            ShowToast($"Загружено отложенных корзин: {entries.Count}.");
        }
        catch (ApiException ex)
        {
            MessageBox.Show(ex.Message, "Отложенные", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show(
                string.IsNullOrWhiteSpace(ex.Message) ? "Нет сети." : ex.Message,
                "Отложенные",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SyncDiscountFieldsFromCart()
    {
        if (!App.Cart.HasCart || App.Cart.Root.ValueKind != JsonValueKind.Object)
        {
            OrderDiscountSummaryText.Text = "Скидка не задана.";
            return;
        }

        var pct = GetCurrentOrderDiscountPercent();
        var sum = GetCurrentOrderDiscountSum();
        OrderDiscountSummaryText.Text = !string.IsNullOrEmpty(pct)
            ? $"Сейчас: {pct}%"
            : !string.IsNullOrEmpty(sum)
                ? $"Сейчас: {sum} сом"
                : "Скидка не задана.";
    }

    private string GetCurrentOrderDiscountPercent()
    {
        if (!App.Cart.HasCart || App.Cart.Root.ValueKind != JsonValueKind.Object)
            return "";

        var c = App.Cart.Root;
        return c.TryGetProperty("order_discount_percent", out var p) ? FormatDiscountScalar(p) : "";
    }

    private string GetCurrentOrderDiscountSum()
    {
        if (!App.Cart.HasCart || App.Cart.Root.ValueKind != JsonValueKind.Object)
            return "";

        var c = App.Cart.Root;
        return c.TryGetProperty("order_discount_total", out var t) ? FormatDiscountMoney(t) : "";
    }

    private static string FormatDiscountScalar(JsonElement v)
    {
        var s = v.ValueKind switch
        {
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.String => v.GetString() ?? "",
            _ => "",
        };
        if (s.Length > 0 && OrderDiscountHelper.IsEmptyOrZeroLike(s))
            return "";
        return s;
    }

    private static string FormatDiscountMoney(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
            return Math.Abs(d) < 1e-9 ? "" : d.ToString("0.00", CultureInfo.InvariantCulture);
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString() ?? "";
            return OrderDiscountHelper.IsEmptyOrZeroLike(s) ? "" : s;
        }

        return "";
    }

    private async Task FinalizeReceiptAsync(string paymentMethod, string cashReceived, bool wantPrintReceipt)
    {
        bool sent = false;
        try
        {
            var body = new Dictionary<string, string>
            {
                ["payment_method"] = paymentMethod ?? "",
                ["print_receipt"] = wantPrintReceipt ? "true" : "false",
                ["cash_received"] = cashReceived ?? ""
            };
            CheckoutResponseHelper.FormatSuccess(
                await App.Api.PosCheckoutAsync(App.Cart.CartId!, body, CancellationToken.None)
                    .ConfigureAwait(true));
            sent = true;
        }
        catch
        {
            // оплата онлайн не прошла, уходим в офлайн-очередь
        }

        if (!sent)
        {
            var entry = new OfflineSaleEntry
            {
                Id = Guid.NewGuid().ToString(),
                PaymentMethod = paymentMethod ?? "",
                CashReceived = cashReceived,
                CartJson = App.Cart.Root.GetRawText(),
                CartId = App.Cart.CartId,
                ShiftId = App.ActiveShiftId,
                BranchId = App.Api.ActiveBranchId,
                CashboxId = App.PosCashboxId,
                Status = "pending_sync",
                CreatedAt = DateTimeOffset.Now
            };
            OfflinePendingSalesStore.Append(entry);
            PosLogger.Log("Офлайн-чек сохранён, ID: " + entry.Id, "OFFLINE");
            CartMessageText.Text = "Чек сохранён локально. Будет отправлен при появлении сети.";
            CartMessageText.Foreground = UiWarn;
        }

        if (wantPrintReceipt && UserPreferences.Instance.ReceiptEnabled)
        {
            var cfg = UserPreferences.Instance.ToReceiptPrinterSettings();
            if (cfg.Enabled && !string.IsNullOrWhiteSpace(cfg.DevicePath))
            {
                try
                {
                    var txt = CartReceiptTextBuilder.BuildSimpleReceipt(
                        App.Cart.Root.GetRawText(),
                        paymentMethodKey: paymentMethod ?? "—",
                        cashReceived: cashReceived);
                    EscPosTextReceiptPrinter.Print(cfg, txt);
                }
                catch (Exception ex)
                {
                    PosLogger.Log("Ошибка печати: " + ex.Message, "PRINTER");
                }
            }
        }

        _ = Task.Run(() => App.OfflineSync.TriggerSyncNowAsync(CancellationToken.None));
    }

    private async Task RefreshShiftStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var list = await App.Api.ConstructionShiftsListAsync(cancellationToken).ConfigureAwait(true);
            var openId = ShiftHelper.PickOpenShiftId(list, App.PosCashboxId);
            // Всегда приводим к факту с сервера: при закрытой смене сбрасываем id (не доверяем старому id из корзины).
            App.ActiveShiftId = string.IsNullOrEmpty(openId) ? null : openId;
        }
        catch
        {
            /* список смен может быть недоступен — не сбрасываем уже известный id */
        }

        UpdateShiftBanner();
    }

    /// <summary>
    /// Смена должна быть открыта на кассе. Перед проверкой запрашивает актуальный список смен с API.
    /// </summary>
    private async Task<bool> EnsureShiftReadyForOperationsAsync(bool silent, CancellationToken cancellationToken = default)
    {
        await RefreshShiftStateAsync(cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrEmpty(App.ActiveShiftId))
            return true;

        const string shortMsg = "Смена не открыта — нажмите «Открыть смену» в шапке.";
        const string longMsg =
            "Смена не открыта на этой кассе.\n\n" +
            "Нажмите «Открыть смену» в шапке окна (при необходимости укажите остаток в кассе).\n\n" +
            "Без открытой смены эта операция недоступна.";
        CartMessageText.Text = shortMsg;
        CartMessageText.Foreground = UiWarn;
        if (!silent)
            MessageBox.Show(this, longMsg, "Смена", MessageBoxButton.OK, MessageBoxImage.Warning);
        PosLogger.Log("Операция отклонена: смена не открыта", "SHIFT");
        return false;
    }

    private void UpdateShiftBanner()
    {
        string tip;
        if (!string.IsNullOrEmpty(App.ActiveShiftId))
        {
            ShiftBannerBar.Background = ThemeBrush("BrushShiftOpenBg", FallbackShiftOpenBg);
            ShiftBannerBar.BorderBrush = ThemeBrush("BrushShiftOpenBorder", FallbackShiftOpenBorder);
            var desk = App.PosCashboxDisplayName ?? App.PosCashboxId ?? "—";
            tip = $"Смена открыта. Касса: {desk}.";
            CloseShiftButton.IsEnabled = true;
        }
        else
        {
            ShiftBannerBar.Background = ThemeBrush("BrushShiftWarnBg", FallbackShiftWarnBg);
            ShiftBannerBar.BorderBrush = ThemeBrush("BrushShiftWarnBorder", FallbackShiftWarnBorder);
            tip =
                "Смена не открыта на этой кассе — нажмите «Открыть смену» (иначе открытие нового чека может вернуть ошибку).";
            CloseShiftButton.IsEnabled = false;
        }

        ShiftBannerBar.ToolTip = tip;
        ShiftOpenForSale = !string.IsNullOrEmpty(App.ActiveShiftId);
        SetScanBusy(_isUiBusy);
    }

    private async void OpenShift_Click(object sender, RoutedEventArgs e)
    {
        OpenShiftButton.IsEnabled = false;
        CloseShiftButton.IsEnabled = false;
        try
        {
            var cb = await EnsurePosCashboxIdAsync().ConfigureAwait(true);
            if (string.IsNullOrEmpty(cb))
            {
                MessageBox.Show(
                    "Не удалось определить кассу (список касс пуст или недоступен).",
                    "Смена",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dlg = new OpenShiftDialog { Owner = this };
            if (dlg.ShowDialog() != true)
                return;

            var opening = string.IsNullOrWhiteSpace(dlg.OpeningCash) ? "0.00" : dlg.OpeningCash;
            var resp = await App.Api.ConstructionShiftOpenAsync(cb, opening).ConfigureAwait(true);
            var sid = CartDisplayHelper.TryShiftIdFromOpenResponse(resp);
            if (!string.IsNullOrEmpty(sid))
                App.ActiveShiftId = sid;
            else
                await RefreshShiftStateAsync().ConfigureAwait(true);

            UpdateShiftBanner();
            MessageBox.Show("Смена открыта.", "Смена", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (ApiException ex)
        {
            MessageBox.Show(ex.Message, "Смена", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show(
                string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message,
                "Смена",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (TaskCanceledException)
        {
            MessageBox.Show("Таймаут запроса.", "Смена", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            OpenShiftButton.IsEnabled = true;
            CloseShiftButton.IsEnabled = !string.IsNullOrEmpty(App.ActiveShiftId);
        }
    }

    private async void CloseShift_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(App.ActiveShiftId))
            return;

        var dlg = new CloseShiftDialog { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        OpenShiftButton.IsEnabled = false;
        CloseShiftButton.IsEnabled = false;
        try
        {
            await App.Api.ConstructionShiftCloseAsync(App.ActiveShiftId, dlg.ClosingCashOrNull).ConfigureAwait(true);
            App.ActiveShiftId = null;
            await RefreshShiftStateAsync().ConfigureAwait(true);
            MessageBox.Show("Смена закрыта.", "Смена", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (ApiException ex)
        {
            MessageBox.Show(ex.Message, "Смена", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show(
                string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message,
                "Смена",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (TaskCanceledException)
        {
            MessageBox.Show("Таймаут запроса.", "Смена", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            OpenShiftButton.IsEnabled = true;
            CloseShiftButton.IsEnabled = !string.IsNullOrEmpty(App.ActiveShiftId);
            UpdateShiftBanner();
        }
    }

    private async Task<string?> EnsurePosCashboxIdAsync(CancellationToken cancellationToken = default)
    {
        var cb = App.PosCashboxId;
        if (!string.IsNullOrWhiteSpace(cb))
            return cb;
        var rawList = await App.Api.ConstructionCashboxesListAsync(cancellationToken).ConfigureAwait(true);
        if (CartDisplayHelper.TryFirstCashbox(rawList, out var id, out var displayName))
        {
            cb = id;
            App.PosCashboxId = id;
            App.PosCashboxDisplayName = displayName;
        }

        return cb;
    }

    private async void StartSale_Click(object sender, RoutedEventArgs e)
    {
        if (HasActiveCartLines)
        {
            var answer = MessageBox.Show(
                this,
                "В текущем чеке уже есть товары.\n\nОткрыть новый пустой чек?",
                "Новый чек",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return;

        CartMessageText.Text = "";
        StartSaleButton.IsEnabled = false;
        RefreshCartButton.IsEnabled = false;
        CheckoutFooterButton.IsEnabled = false;
        ScanBarcodeButton.IsEnabled = false;
        BarcodeBox.IsEnabled = false;
        try
        {
            await TryStartNewSaleAsync().ConfigureAwait(true);
            RebindCartUi();
            CartMessageText.Text = "Новый чек открыт.";
            CartMessageText.Foreground = UiOk;
        }
        catch (ApiException ex)
        {
            CartMessageText.Text = ex.Message;
            CartMessageText.Foreground = UiWarn;
        }
        catch (HttpRequestException ex)
        {
            CartMessageText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
            CartMessageText.Foreground = UiWarn;
        }
        catch (TaskCanceledException)
        {
            CartMessageText.Text = "Таймаут запроса.";
            CartMessageText.Foreground = UiWarn;
        }
        finally
        {
            StartSaleButton.IsEnabled = true;
            RefreshCartButton.IsEnabled = App.Cart.CanRefresh;
            CheckoutFooterButton.IsEnabled = App.Cart.CanRefresh && CartLines.Count > 0;
            ScanBarcodeButton.IsEnabled = App.Cart.CanRefresh;
            BarcodeBox.IsEnabled = App.Cart.CanRefresh;
        }
    }

    private async Task TryStartNewSaleAsync(CancellationToken cancellationToken = default)
    {
        var cb = await EnsurePosCashboxIdAsync(cancellationToken).ConfigureAwait(true);
        var cart = await App.Api.PosSalesStartAsync(string.IsNullOrWhiteSpace(cb) ? null : cb, cancellationToken).ConfigureAwait(true);
        App.Cart.SetCart(cart);
    }

    /// <summary>После успешной оплаты: сброс локальной корзины и автоматическое открытие нового чека.</summary>
    /// <returns>null при успехе; иначе краткий текст ошибки для пользователя.</returns>
    private async Task<string?> TryRestartSaleSessionAfterCheckoutAsync()
    {
        App.Cart.Clear();
        try
        {
            await RefreshShiftStateAsync().ConfigureAwait(true);
            if (string.IsNullOrEmpty(App.ActiveShiftId))
            {
                RebindCartUi();
                const string msg =
                    "Новый чек не открыт: смена не открыта на этой кассе. Откройте смену и нажмите «Новый чек».";
                PosLogger.Log(msg, "SHIFT");
                return msg;
            }

            await TryStartNewSaleAsync().ConfigureAwait(true);
            RebindCartUi();
            return null;
        }
        catch (ApiException ex)
        {
            RebindCartUi();
            PosLogger.Log($"После оплаты: не удалось начать продажу (API): {ex.Message}", "PAYMENT");
            return ex.Message;
        }
        catch (HttpRequestException ex)
        {
            RebindCartUi();
            var t = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
            PosLogger.Log($"После оплаты: не удалось начать продажу (сеть): {t}", "PAYMENT");
            return t;
        }
        catch (TaskCanceledException)
        {
            RebindCartUi();
            PosLogger.Log("После оплаты: не удалось начать продажу (таймаут).", "PAYMENT");
            return "Таймаут запроса.";
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control) || m.HasFlag(ModifierKeys.Alt) || m.HasFlag(ModifierKeys.Windows))
            return;

        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox)
            return;

        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            if (_barcodeBuf.Length >= MinBarcodeLen)
            {
                e.Handled = true;
                var code = _barcodeBuf;
                _barcodeBuf = "";
                _ = RunScanAsync(code);
            }
            else
                _barcodeBuf = "";

            return;
        }

        var shift = m.HasFlag(ModifierKeys.Shift);
        var ch = KeyToBarcodeChar(e.Key, shift);
        if (ch == null)
            return;

        var now = Environment.TickCount64;
        var delta = now - _barcodeLastTick;
        if (delta < 0 || delta > BarcodeInterkeyMs)
            _barcodeBuf = "";
        _barcodeLastTick = now;

        _barcodeBuf += ch;
        if (_barcodeBuf.Length > BarcodeMaxLen)
            _barcodeBuf = _barcodeBuf.Substring(_barcodeBuf.Length - BarcodeMaxLen);

        e.Handled = true;
    }

    private static string? KeyToBarcodeChar(Key key, bool shift)
    {
        if (key is >= Key.D0 and <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return ((char)('0' + (key - Key.NumPad0))).ToString();

        if (key is >= Key.A and <= Key.Z)
        {
            var c = (char)('a' + (key - Key.A));
            if (shift)
                c = char.ToUpperInvariant(c);
            return c.ToString();
        }

        if (key == Key.Space)
            return " ";

        if (key == Key.OemMinus || key == Key.Subtract)
            return "-";

        if (key == Key.OemPeriod || key == Key.Decimal)
            return ".";

        return null;
    }

    /// <summary>null — печать ок; иначе текст предупреждения для пользователя.</summary>
    private async Task<string?> TryPrintReceiptAfterCheckoutAsync(JsonElement checkoutResponse, bool wantPrintReceipt)
    {
        if (!wantPrintReceipt)
            return null;

        PosLogger.Log("Печать чека после оплаты: запрос включён", "PRINTER");

        var cfg = UserPreferences.Instance.ToReceiptPrinterSettings();
        if (!cfg.Enabled)
        {
            PosLogger.Log("Печать: выключена в настройках кассы", "PRINTER");
            return "Печать: выключено в «Настройки кассы». Включите печать и укажите LPT.";
        }

        var txt = CheckoutResponseHelper.TryReceiptTextFromCheckout(checkoutResponse);
        if (string.IsNullOrWhiteSpace(txt))
        {
            var saleId = CheckoutResponseHelper.TrySaleId(checkoutResponse);
            if (!string.IsNullOrEmpty(saleId))
            {
                try
                {
                    PosLogger.Log($"GET receipt для sale_id={saleId}", "PRINTER");
                    var rec = await App.Api.PosSaleReceiptAsync(saleId).ConfigureAwait(true);
                    txt = CheckoutResponseHelper.TryReceiptTextFromSaleReceiptPayload(rec);
                }
                catch (ApiException ex)
                {
                    PosLogger.Log($"GET receipt ApiException: {ex.Message}", "ERROR");
                    return $"Печать: не удалось загрузить чек (GET receipt): {ex.Message}";
                }
                catch (HttpRequestException ex)
                {
                    PosLogger.Log($"GET receipt Http: {ex.Message}", "ERROR");
                    return $"Печать: сеть при загрузке чека: {ex.Message}";
                }
            }
        }

        if (string.IsNullOrWhiteSpace(txt))
        {
            PosLogger.Log("Печать: пустой текст чека после API", "PRINTER");
            return "Печать: не удалось получить текст чека.";
        }

        try
        {
            PosLogger.Log($"LPT печать: устройство={cfg.DevicePath}, символов текста={txt.Length}", "PRINTER");
            EscPosTextReceiptPrinter.Print(cfg, txt);
            PosLogger.Log("LPT: отправка завершена без исключения", "PRINTER");
            return null;
        }
        catch (Exception ex)
        {
            PosLogger.Log($"Печать LPT: {ex.Message}", "ERROR");
            return $"Печать: {ex.Message}";
        }
    }

    private async void Checkout_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Checkout_ClickCoreAsync();
        }
        catch (Exception ex)
        {
            PosLogger.Log($"Checkout_Click внешний catch: {ex.Message} | {ex.StackTrace}", "ERROR");
            try
            {
                MessageBox.Show(
                    "Сбой при оплате:\n\n" + ex.Message,
                    "Оплата",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private async Task Checkout_ClickCoreAsync()
    {
        PosLogger.Log("Начало процесса оплаты", "PAYMENT");

        if (App.Api is null)
        {
            PosLogger.Log("Оплата: Api == null", "ERROR");
            MessageBox.Show("Подключение не готово.", "Оплата", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!App.Cart.HasCart || !App.Cart.CanRefresh || CartLines.Count == 0)
        {
            PosLogger.Log("Оплата: нет корзины или пусто", "PAYMENT");
            MessageBox.Show("Добавьте товары в корзину.", "Оплата", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!await EnsureShiftReadyForOperationsAsync(false).ConfigureAwait(true))
            return;

        var total = CartDisplayHelper.TotalDue(App.Cart.Root);
        var dlg = new CheckoutDialog(total) { Owner = this };
        if (dlg.ShowDialog() != true)
        {
            PosLogger.Log("Оплата: диалог отменён", "PAYMENT");
            return;
        }

        var paymentMethod = dlg.PaymentMethodKey;
        var wantPrintReceipt = dlg.WantPrintReceipt;
        var cashReceived = dlg.CashReceivedForApi;

        PosLogger.Log(
            $"Диалог оплаты OK: method={paymentMethod}, cash_received={cashReceived}, print_receipt={wantPrintReceipt}, total={total}",
            "PAYMENT");

        CartMessageText.Text = "";
        SetScanBusy(true);
        try
        {
            await FinalizeReceiptAsync(paymentMethod, cashReceived, wantPrintReceipt).ConfigureAwait(true);

            var restartErr = await TryRestartSaleSessionAfterCheckoutAsync().ConfigureAwait(true);
            if (restartErr != null)
            {
                CartMessageText.Text = "Нажмите «Новый чек», чтобы продолжить.";
                CartMessageText.Foreground = UiWarn;
            }
            else
            {
                CartMessageText.Text = "Новый чек открыт — добавьте товары.";
                CartMessageText.Foreground = UiOk;
            }

            await RefreshShiftStateAsync().ConfigureAwait(true);
        }
        catch (ApiException ex)
        {
            PosLogger.Log($"Оплата ApiException: {ex.Message}", "ERROR");
            MessageBox.Show(ex.Message, "Оплата", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (HttpRequestException ex)
        {
            PosLogger.Log($"Оплата HttpRequestException: {ex.Message}", "ERROR");
            await HandleAutomaticOfflineCheckoutAsync(paymentMethod, cashReceived, wantPrintReceipt, ex.Message).ConfigureAwait(true);
        }
        catch (JsonException ex)
        {
            PosLogger.Log($"Оплата JsonException: {ex.Message}", "ERROR");
            MessageBox.Show("Сервер вернул ответ, который не удалось разобрать как JSON (оплата могла пройти или нет).\n\n" + ex.Message,
                "Оплата", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (OperationCanceledException)
        {
            PosLogger.Log("Оплата: отмена / таймаут", "ERROR");
            await HandleAutomaticOfflineCheckoutAsync(paymentMethod, cashReceived, wantPrintReceipt, "Таймаут оплаты или потеря сети.")
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            PosLogger.Log($"Оплата Exception: {ex.Message} | {ex.StackTrace}", "ERROR");
            MessageBox.Show("Неожиданная ошибка при оплате:\n\n" + ex.Message, "Оплата", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private void ForceClearCartDisplay()
    {
        CartLines.Clear();
        CartTotalAmountText.Text = "0.00";
        CartItemsCountText.Text = "0";
        CheckoutFooterButton.IsEnabled = false;
        CartMessageText.Text = "";
        CartMessageText.Foreground = UiMuted;
        App.Cart.Clear();
        OrderDiscountSummaryText.Text = "Скидка не задана.";
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e) => CatalogSearchBox.Text = "";

    private void OnCatalogCacheChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // При любом изменении кэша полностью перестраиваем каталог.
        UpdateCatalogDisplay();
    }

    private void UpdateCatalogDisplay()
    {
        var products = CatalogCacheService.Products;
        // Разделяем на весовые и штучные
        var kg = products.Where(p => p.MustWeigh).ToList();
        var piece = products.Where(p => !p.MustWeigh).ToList();

        // Очищаем основные списки
        _allTilesKg.Clear();
        _allTilesKg.AddRange(kg);
        _allTilesPiece.Clear();
        _allTilesPiece.AddRange(piece);

        // Перестраиваем отображаемые коллекции с учётом текущего viewport'а
        // Сначала очищаем
        _tilesKg.Clear();
        _tilesPiece.Clear();

        // Применяем фильтр, если он есть
        var filteredKg = _currentFilter != null
            ? _allTilesKg.Where(vm => FilterPredicate(vm)).ToList()
            : _allTilesKg;
        var filteredPiece = _currentFilter != null
            ? _allTilesPiece.Where(vm => FilterPredicate(vm)).ToList()
            : _allTilesPiece;

        // Обновляем отображаемые плитки с учётом viewport'а (но ResetCatalogViewport обнулит счетчики, поэтому мы аккуратно перезаполним)
        // Используем EnsureActiveCatalogPageVisible для вычисления видимого количества
        _visibleKgCount = Math.Min(_visibleKgCount == 0 ? CatalogInitialRenderCount : _visibleKgCount, filteredKg.Count);
        _visiblePieceCount = Math.Min(_visiblePieceCount == 0 ? CatalogInitialRenderCount : _visiblePieceCount, filteredPiece.Count);

        SyncVisibleTiles(_tilesKg, filteredKg, _visibleKgCount);
        SyncVisibleTiles(_tilesPiece, filteredPiece, _visiblePieceCount);

        // Сортировка избранного, обновление счётчиков и уведомление
        SortByFavorite();
        UpdateCatalogCount();
        OnPropertyChanged("CatalogTableSource");
    }

    private async Task HandleAutomaticOfflineCheckoutAsync(
        string? paymentMethod,
        string? cashReceived,
        bool wantPrintReceipt,
        string? reason)
    {
        var saved = SaveCurrentSaleOffline(paymentMethod, cashReceived);
        PosLogger.Log($"Оплата переведена в офлайн-очередь: {saved.Id}. Причина: {reason}", "PAYMENT");

        var cfg = UserPreferences.Instance.ToReceiptPrinterSettings();
        if (wantPrintReceipt && UserPreferences.Instance.ReceiptEnabled && cfg.Enabled &&
            !string.IsNullOrWhiteSpace(cfg.DevicePath))
        {
            try
            {
                var txt = CartReceiptTextBuilder.BuildSimpleReceipt(
                    saved.CartJson,
                    "ОФФЛАЙН (ожидает выгрузку)",
                    saved.PaymentMethod,
                    saved.CashReceived);
                EscPosTextReceiptPrinter.Print(cfg, txt);
            }
            catch (Exception ex)
            {
                PosLogger.Log($"Оффлайн-печать после сбоя сети: {ex.Message}", "PRINTER");
            }
        }

        var saleRestartErr = await TryRestartSaleSessionAfterCheckoutAsync().ConfigureAwait(true);
        if (saleRestartErr != null)
        {
            CartMessageText.Text = "Чек сохранён офлайн. " + saleRestartErr;
            CartMessageText.Foreground = UiWarn;
        }
        else
        {
            CartMessageText.Text = "Связь потеряна: чек сохранён офлайн, новый чек открыт.";
            CartMessageText.Foreground = UiOk;
        }

        MessageBox.Show(
            "Связь пропала во время оплаты.\n\n" +
            "Чек автоматически сохранён офлайн и будет обработан после восстановления связи.",
            "Оффлайн-оплата",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OfflineStatus_Click(object sender, RoutedEventArgs e)
    {
        int pending = OfflinePendingSalesStore.PendingCount;
        int failed = OfflinePendingSalesStore.FailedCount;
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                   "NurMarketKassa", "offline_sales_pending.json");
        MessageBox.Show(
            $"Ожидают синхронизации: {pending}\nОшибок синхронизации: {failed}\n\nФайл данных:\n{path}",
            "Оффлайн-продажи", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        var filterWindow = new FilterWindow
        {
            Owner = this,
            Categories = Categories,
            Brands = Brands
        };
        if (filterWindow.ShowDialog() == true)
        {
            ApplyFilterToCatalog(filterWindow.GetFilterCriteria());
        }
    }

    private async void RefreshCart_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Cart.CanRefresh)
            return;

        CartMessageText.Text = "";
        RefreshCartButton.IsEnabled = false;
        CheckoutFooterButton.IsEnabled = false;
        ScanBarcodeButton.IsEnabled = false;
        BarcodeBox.IsEnabled = false;
        try
        {
            await ReloadCartFromServerAsync().ConfigureAwait(true);
            RebindCartUi();
            CartMessageText.Text = "Корзина обновлена.";
            CartMessageText.Foreground = UiOk;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            App.Cart.Clear();
            RebindCartUi();
            CartMessageText.Text = "Корзина не найдена — начните продажу заново.";
            CartMessageText.Foreground = UiWarn;
        }
        catch (ApiException ex)
        {
            CartMessageText.Text = ex.Message;
            CartMessageText.Foreground = UiWarn;
        }
        catch (HttpRequestException ex)
        {
            CartMessageText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
            CartMessageText.Foreground = UiWarn;
        }
        catch (TaskCanceledException)
        {
            CartMessageText.Text = "Таймаут запроса.";
            CartMessageText.Foreground = UiWarn;
        }
        finally
        {
            SetScanBusy(_isUiBusy);
        }
    }

    private async Task ReloadCartFromServerAsync()
    {
        if (!App.Cart.CanRefresh)
            return;
        var c = await App.Api.PosCartGetAsync(App.Cart.CartId!).ConfigureAwait(true);
        App.Cart.SetCart(c);
    }

    private void BarcodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        _ = RunScanAsync(BarcodeBox.Text);
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CatalogProductTileVm vm)
        {
            vm.IsFavorite = !vm.IsFavorite;
            SortByFavorite();
            ApplyCatalogViewport();
            OnPropertyChanged("CatalogTableSource");
        }
    }

    private void ApplyFilterToCatalog(FilterCriteria criteria)
    {
        _currentFilter = criteria;
        SortByFavorite();

        var filtered = new ObservableCollection<CatalogProductTileVm>(
            _allTilesKg.Concat(_allTilesPiece).Where(new Func<CatalogProductTileVm, bool>(FilterPredicate)));

        _tilesKg.Clear();
        foreach (var t in filtered.Where(t => t.MustWeigh)) _tilesKg.Add(t);
        _tilesPiece.Clear();
        foreach (var t in filtered.Where(t => !t.MustWeigh)) _tilesPiece.Add(t);

        UpdateCatalogCount();
        OnPropertyChanged("CatalogTableSource");
    }

    private bool FilterPredicate(object obj)
    {
        if (obj is not CatalogProductTileVm vm) return false;
        if (_currentFilter == null) return true;

        bool catOk = string.IsNullOrEmpty(_currentFilter.Category) ||
                     string.Equals(vm.Category, _currentFilter.Category, StringComparison.OrdinalIgnoreCase);
        bool brandOk = string.IsNullOrEmpty(_currentFilter.Brand) ||
                       string.Equals(vm.Brand, _currentFilter.Brand, StringComparison.OrdinalIgnoreCase);
        bool weightOk = !_currentFilter.OnlyWeight || vm.MustWeigh;
        bool stockOk = !_currentFilter.OnlyInStock || !string.IsNullOrEmpty(vm.StockInfo);
        bool favOk = !_currentFilter.OnlyFavorite || vm.IsFavorite;

        return catOk && brandOk && weightOk && stockOk && favOk;
    }

    private void SortByFavorite()
    {
        _allTilesKg.Sort((a, b) => b.IsFavorite.CompareTo(a.IsFavorite));
        _allTilesPiece.Sort((a, b) => b.IsFavorite.CompareTo(a.IsFavorite));

        var kg = _tilesKg.OrderByDescending(vm => vm.IsFavorite).ToList();
        var piece = _tilesPiece.OrderByDescending(vm => vm.IsFavorite).ToList();

        _tilesKg.Clear();
        foreach (var t in kg) _tilesKg.Add(t);
        _tilesPiece.Clear();
        foreach (var t in piece) _tilesPiece.Add(t);

        OnPropertyChanged("CatalogTableSource");
    }

    private void UpdateCatalogCount()
    {
        if (CatalogCountText == null) return;
        int total = _allTilesKg.Count + _allTilesPiece.Count;
        int shown = _tilesKg.Count + _tilesPiece.Count;
        CatalogCountText.Text = total == 0 ? "Нет товаров" : $"{shown}/{total}";
    }

    private async void CatalogProduct_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CatalogProductTileVm vm)
        {
            if (UserPreferences.Instance.SingleClickToCart || e.ClickCount == 2)
                await PickProductFromCatalogAsync(vm);
        }
    }
    private async void ScanBarcode_Click(object sender, RoutedEventArgs e) => await RunScanAsync(BarcodeBox.Text);

    private async Task RunScanAsync(string? rawCode)
    {
        var code = (rawCode ?? "").Trim();
        if (code.Length == 0)
            return;

        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return;
        if (!await EnsureSaleSessionReadyAsync().ConfigureAwait(true))
            return;

        CartMessageText.Text = "";
        SetScanBusy(true);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var resp = await App.Api.PosScanAsync(App.Cart.CartId!, code).ConfigureAwait(true);
                    if (!CartResponseHelper.TryUpdateCartSession(resp, App.Cart))
                        await ReloadCartFromServerAsync().ConfigureAwait(true);
                    RebindCartUi();
                    CartMessageText.Text = "Товар добавлен.";
                    CartMessageText.Foreground = UiOk;
                    BarcodeBox.Text = "";
                    BarcodeBox.Focus();
                    return;
                }
                catch (ApiException ex) when (attempt == 0 && CartResponseHelper.LooksLikeStaleCart(ex))
                {
                    try
                    {
                        await TryStartNewSaleAsync().ConfigureAwait(true);
                        RebindCartUi();
                        CartMessageText.Text = "Корзина устарела — открыта новая продажа, повторяем скан.";
                        CartMessageText.Foreground = UiOk;
                    }
                    catch (ApiException rex)
                    {
                        var ru = PosErrorMessages.UserMessageForCatalogOrScan(rex);
                        CartMessageText.Text = ru;
                        CartMessageText.Foreground = UiWarn;
                        ShowToast(ru, warn: true);
                        return;
                    }
                }
            }

            CartMessageText.Text = "Не удалось отсканировать после повтора.";
            CartMessageText.Foreground = UiWarn;
            ShowToast("Не удалось отсканировать после повтора.", warn: true);
        }
        catch (ApiException ex)
        {
            var ru = PosErrorMessages.UserMessageForCatalogOrScan(ex);
            CartMessageText.Text = ru;
            CartMessageText.Foreground = UiWarn;
            ShowToast(ru, warn: true);
        }
        catch (HttpRequestException ex)
        {
            CartMessageText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
            CartMessageText.Foreground = UiWarn;
        }
        catch (TaskCanceledException)
        {
            CartMessageText.Text = "Скан: таймаут (проверьте сеть).";
            CartMessageText.Foreground = UiWarn;
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private void SetScanBusy(bool busy)
    {
        _isUiBusy = busy;
        var can = App.Cart.CanRefresh;
        var shiftOk = ShiftOpenForSale;
        var hasLines = CartLines?.Count > 0;
        var deferredCount = DeferredCartsStore.Count();
        var offlineQueue = OfflinePendingSalesStore.PendingCount + OfflinePendingSalesStore.FailedCount;
        if (ScanBarcodeButton != null)
            ScanBarcodeButton.IsEnabled = !busy && can && shiftOk;
        if (BarcodeBox != null)
            BarcodeBox.IsEnabled = !busy && can && shiftOk;
        if (StartSaleButton != null)
            StartSaleButton.IsEnabled = !busy;
        if (RefreshCartButton != null)
            RefreshCartButton.IsEnabled = !busy && can;
        if (CheckoutFooterButton != null)
            CheckoutFooterButton.IsEnabled = !busy && can && hasLines == true;
        if (OrderDiscountButton != null)
            OrderDiscountButton.IsEnabled = !busy && can;
        if (DeferCartButton != null)
            DeferCartButton.IsEnabled = !busy && can && hasLines == true;
        if (RestoreLatestDeferredButton != null)
            RestoreLatestDeferredButton.IsEnabled = !busy && deferredCount > 0;
        if (OpenDeferredCartsButton != null)
            OpenDeferredCartsButton.IsEnabled = !busy && deferredCount > 0;
        //if (OfflineSaleButton != null)
        //    OfflineSaleButton.IsEnabled = !busy && can && hasLines == true;
        //if (OfflineQueueButton != null)
        //    OfflineQueueButton.IsEnabled = !busy && offlineQueue > 0;
    }

    private async void CartQtyMinus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not CartLineRow row)
            return;
        await AdjustLineQtyAsync(row, -1).ConfigureAwait(true);
    }

    private async void CartQtyPlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not CartLineRow row)
            return;
        await AdjustLineQtyAsync(row, 1).ConfigureAwait(true);
    }

    private async void CartLineDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not CartLineRow row)
            return;
        if (!App.Cart.CanRefresh || string.IsNullOrEmpty(row.ItemId))
            return;
        await DeleteLineAsync(row.ItemId).ConfigureAwait(true);
    }

    private async void CartWeigh_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not CartLineRow row || !row.WeighedLine)
            return;
        if (!App.Cart.CanRefresh || string.IsNullOrEmpty(row.ItemId))
            return;

        var initial = FormatQtySubline(row.Qty, true);
        var dlg = new WeighedProductDialog(
            row.Title,
            row.PricePerKgHint,
            _scaleService,
            initialKg: initial,
            okButtonText: "Применить",
            windowTitle: "Изменить вес")
        { Owner = this };

        if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.QuantityNormalized))
            return;

        SetScanBusy(true);
        try
        {
            var resp = await App.Api
                .PosCartItemPatchAsync(
                    App.Cart.CartId!,
                    row.ItemId,
                    new Dictionary<string, string> { ["quantity"] = dlg.QuantityNormalized })
                .ConfigureAwait(true);
            if (!CartResponseHelper.TryUpdateCartSession(resp, App.Cart))
                await ReloadCartFromServerAsync().ConfigureAwait(true);
            CartMessageText.Text = "";
            RebindCartUi();
        }
        catch (ApiException ex)
        {
            CartMessageText.Text = ex.Message;
            CartMessageText.Foreground = UiWarn;
        }
        catch (HttpRequestException ex)
        {
            CartMessageText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
            CartMessageText.Foreground = UiWarn;
        }
        catch (TaskCanceledException)
        {
            CartMessageText.Text = "Таймаут запроса.";
            CartMessageText.Foreground = UiWarn;
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private async Task AdjustLineQtyAsync(CartLineRow row, int direction)
    {
        if (!App.Cart.CanRefresh || string.IsNullOrEmpty(row.ItemId))
            return;

        var step = row.WeighedLine ? 0.1 : 1.0;
        var q = Math.Round(row.Qty + direction * step, 4);
        if (q <= 0)
        {
            await DeleteLineAsync(row.ItemId).ConfigureAwait(true);
            return;
        }

        var qtyStr = FormatQuantityForApi(q, row.WeighedLine);
        SetScanBusy(true);
        try
        {
            var resp = await App.Api
                .PosCartItemPatchAsync(
                    App.Cart.CartId!,
                    row.ItemId,
                    new Dictionary<string, string> { ["quantity"] = qtyStr })
                .ConfigureAwait(true);
            if (!CartResponseHelper.TryUpdateCartSession(resp, App.Cart))
                await ReloadCartFromServerAsync().ConfigureAwait(true);
            CartMessageText.Text = "";
            RebindCartUi();
        }
        catch (ApiException ex)
        {
            CartMessageText.Text = ex.Message;
            CartMessageText.Foreground = UiWarn;
        }
        catch (HttpRequestException ex)
        {
            CartMessageText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
            CartMessageText.Foreground = UiWarn;
        }
        catch (TaskCanceledException)
        {
            CartMessageText.Text = "Таймаут запроса.";
            CartMessageText.Foreground = UiWarn;
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private async Task DeleteLineAsync(string itemId)
    {
        SetScanBusy(true);
        try
        {
            await App.Api.PosCartItemDeleteAsync(App.Cart.CartId!, itemId).ConfigureAwait(true);
            await ReloadCartFromServerAsync().ConfigureAwait(true);
            CartMessageText.Text = "";
            RebindCartUi();
        }
        catch (ApiException ex)
        {
            CartMessageText.Text = ex.Message;
            CartMessageText.Foreground = UiWarn;
        }
        catch (HttpRequestException ex)
        {
            CartMessageText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
            CartMessageText.Foreground = UiWarn;
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private static string FormatQuantityForApi(double q, bool weighed)
    {
        if (weighed)
        {
            var s = q.ToString("0.####", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
            return string.IsNullOrEmpty(s) ? "0" : s;
        }

        return Math.Round(q, 0).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatQtySubline(double qty, bool weighed)
    {
        if (weighed)
        {
            var s = qty.ToString("0.###", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
            return string.IsNullOrEmpty(s) ? "0" : s;
        }

        return Math.Round(qty, 0).ToString(CultureInfo.InvariantCulture);
    }

    private void RebindCartUi()
    {
        CartLines.Clear();
        if (!App.Cart.HasCart)
        {
            CartTotalAmountText.Text = "";
            RefreshCartButton.IsEnabled = false;
            CheckoutFooterButton.IsEnabled = false;
            ScanBarcodeButton.IsEnabled = false;
            BarcodeBox.IsEnabled = false;
            SyncDiscountFieldsFromCart();
            UpdateShiftBanner();
            UpdateCartStateUi();
            UpdateDeferredCartUi();
            SetScanBusy(_isUiBusy);
            return;
        }

        var root = App.Cart.Root;
        foreach (var it in CartDisplayHelper.EnumerateItems(root))
        {
            var iid = CartDisplayHelper.TryItemId(it);
            if (string.IsNullOrEmpty(iid))
                continue;

            var weighed = CartDisplayHelper.LineMustWeigh(it);
            var qtyVal = CartDisplayHelper.LineQuantity(it);
            var up = CartDisplayHelper.UnitPrice(it);
            var unit = weighed ? "кг" : "шт";
            var sub = $"{FormatQtySubline(qtyVal, weighed)} {unit} × {CartDisplayHelper.FormatMoney(up)} сом";
            var priceKg = weighed ? $"{CartDisplayHelper.FormatMoney(up)} сом" : "";

            CartLines.Add(new CartLineRow
            {
                ItemId = iid,
                Qty = qtyVal,
                WeighedLine = weighed,
                Title = CartDisplayHelper.ItemName(it),
                SubLine = sub,
                LineTotal = CartDisplayHelper.LineTotal(it),
                PricePerKgHint = priceKg,
            });
        }

        var total = CartDisplayHelper.TotalDue(root);
        CartTotalAmountText.Text = CartDisplayHelper.FormatMoney(total);
        RefreshCartButton.IsEnabled = App.Cart.CanRefresh;
        CheckoutFooterButton.IsEnabled = App.Cart.CanRefresh && CartLines.Count > 0;
        ScanBarcodeButton.IsEnabled = App.Cart.CanRefresh;
        BarcodeBox.IsEnabled = App.Cart.CanRefresh;
        SyncDiscountFieldsFromCart();
        UpdateShiftBanner();
        UpdateCartStateUi();
        UpdateDeferredCartUi();
        SetScanBusy(_isUiBusy);
    }

    public void ApplyHardwareAndUiPreferences()
    {
        ApplyFullscreenPreference();
        _scaleUiTimer?.Stop();
        _scaleUiTimer = null;
        _scaleService?.Dispose();
        if (UserPreferences.Instance.ScaleEnabled)
        {
            _scaleService = new ScaleReaderService(UserPreferences.Instance.ToScaleSettings());
            _scaleService.Start();
            _scaleUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _scaleUiTimer.Tick += (_, _) => UpdateScaleStatusLine();
            _scaleUiTimer.Start();
        }
        else
        {
            _scaleService = null;
        }
        UpdateScaleStatusLine();
    }

    private void ApplyFullscreenPreference()
    {
        if (UserPreferences.Instance.Fullscreen)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            if (Width < 400)
                Width = 1280;
            if (Height < 300)
                Height = 840;
        }
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var p = UserPreferences.Instance;
        p.DarkTheme = !p.DarkTheme;
        p.SaveToDisk();
        App.ApplyTheme(p.DarkTheme);
        _allowMainWindowClose = true;
        try
        {
            var next = new MainWindow();
            Application.Current.MainWindow = next;
            next.Show();
            Close();
        }
        finally
        {
            _allowMainWindowClose = false;
        }
    }

    private void UpdateThemeButtonIcon()
    {
        if (ThemeToggleGlyph != null)
        {
            // Segoe MDL2 Assets: E706 — яркость (к светлой теме), E708 — тихие часы / ночь (к тёмной)
            ThemeToggleGlyph.Text = UserPreferences.Instance.DarkTheme ? "\uE706" : "\uE708";
        }

        if (ThemeToggleButton != null)
        {
            ThemeToggleButton.ToolTip = UserPreferences.Instance.DarkTheme
                ? "Переключить на светлую тему"
                : "Переключить на тёмную тему";
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PosSettingsWindow { Owner = this };
        dlg.ShowDialog();
    }

    private static string FormatBranchLine(string? branchId) =>
        string.IsNullOrEmpty(branchId)
            ? "Филиал не выбран"
            : "Филиал выбран";

    private static string? TryBranchId(JsonElement user)
    {
        if (user.ValueKind != JsonValueKind.Object)
            return null;

        if (user.TryGetProperty("primary_branch_id", out var pb) && pb.ValueKind == JsonValueKind.String)
        {
            var s = pb.GetString();
            if (!string.IsNullOrEmpty(s))
                return s;
        }

        if (user.TryGetProperty("branch_ids", out var bids) && bids.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in bids.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrEmpty(s))
                        return s;
                }
            }
        }

        return null;
    }

    private static string TryUserLabel(JsonElement user)
    {
        if (user.ValueKind != JsonValueKind.Object)
            return "Пользователь";

        foreach (var key in new[] { "full_name", "name", "email", "username" })
        {
            if (user.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
            {
                var t = p.GetString();
                if (!string.IsNullOrWhiteSpace(t))
                    return t!;
            }
        }

        return "Пользователь";
    }

    private void Logout_Click(object sender, RoutedEventArgs e) => NavigateToLogin();

    private void NavigateToLogin()
    {
        App.Api.ClearSession();
        App.Cart.Clear();
        App.PosCashboxId = null;
        App.PosCashboxDisplayName = null;
        App.ActiveShiftId = null;
        var login = new LoginWindow();
        Application.Current.MainWindow = login;
        login.Show();
        _allowMainWindowClose = true;
        try
        {
            Close();
        }
        finally
        {
            _allowMainWindowClose = false;
        }
    }

    // ────────── Управление наличными (панель смены) ──────────

    private static readonly string CashHistoryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NurMarketKassa", "cash_history.json");

    // Показать панель «Внесение»
    private void ShiftCashInButton_Click(object sender, RoutedEventArgs e)
    {
        ShiftCashOperationPanel.Visibility = Visibility.Visible;
        ShiftCashOperationTitle.Text = "Внесение наличных";
        ShiftCashAmountBox.Text = "";
        ShiftCashCommentBox.Text = "";
        ShiftCashAmountBox.Focus();
    }

    // Показать панель «Изъятие»
    private void ShiftCashOutButton_Click(object sender, RoutedEventArgs e)
    {
        ShiftCashOperationPanel.Visibility = Visibility.Visible;
        ShiftCashOperationTitle.Text = "Изъятие наличных";
        ShiftCashAmountBox.Text = "";
        ShiftCashCommentBox.Text = "";
        ShiftCashAmountBox.Focus();
    }

    // Применить операцию (внесение / изъятие)
    private void ShiftCashApplyButton_Click(object sender, RoutedEventArgs e)
    {
        // Считываем сумму
        if (!decimal.TryParse(ShiftCashAmountBox.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
        {
            MessageBox.Show("Введите корректную сумму.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Определяем тип операции по заголовку
        bool isDeposit = ShiftCashOperationTitle.Text.StartsWith("Внесение");
        decimal signedAmount = isDeposit ? amount : -amount;

        var entry = new FinanceWindow.CashSessionEntry
        {
            CreatedAt = DateTime.Now,
            Type = isDeposit ? "Внесение" : "Изъятие",
            Amount = signedAmount,
            Comment = ShiftCashCommentBox.Text.Trim(),
            UserId = App.CurrentUserId ?? "—"
        };

        // Загружаем, добавляем, сохраняем
        var history = LoadCashHistoryFromDisk();
        history.Add(entry);
        SaveCashHistoryToDisk(history);

        // Очищаем и прячем панель
        ShiftCashAmountBox.Text = "";
        ShiftCashCommentBox.Text = "";
        ShiftCashOperationPanel.Visibility = Visibility.Collapsed;

        ShowToast(isDeposit ? "Внесение записано." : "Изъятие записано.");
    }

    // Отмена операции
    private void ShiftCashCancelButton_Click(object sender, RoutedEventArgs e)
    {
        ShiftCashAmountBox.Text = "";
        ShiftCashCommentBox.Text = "";
        ShiftCashOperationPanel.Visibility = Visibility.Collapsed;
    }

    // Сохранить начальный остаток (сумму при открытии смены)
    private void ShiftSaveOpeningCash_Click(object sender, RoutedEventArgs e)
    {
        string openingText = ShiftOpeningCashBox.Text.Trim();
        if (!decimal.TryParse(openingText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal openingCash) || openingCash < 0)
        {
            MessageBox.Show("Введите корректную сумму начального остатка.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var entry = new FinanceWindow.CashSessionEntry
        {
            CreatedAt = DateTime.Now,
            Type = "Начальный остаток",
            Amount = openingCash,
            Comment = "Установлен при открытии смены",
            UserId = App.CurrentUserId ?? "—"
        };

        var history = LoadCashHistoryFromDisk();
        // Удаляем предыдущие записи о начальном остатке за сегодня (опционально)
        history.Add(entry);
        SaveCashHistoryToDisk(history);

        ShowToast("Начальный остаток сохранён.");
    }

    // История операций с наличными
    private void ShiftOpenCashHistory_Click(object sender, RoutedEventArgs e)
    {
        var history = LoadCashHistoryFromDisk();
        var dlg = new CashHistoryDialog(
            new ObservableCollection<FinanceWindow.CashSessionEntry>(history),
            App.CurrentUserId);
        dlg.Owner = this;
        dlg.ShowDialog();
    }

    // ────────── Вспомогательные методы для работы с файлом ──────────
    private static List<FinanceWindow.CashSessionEntry> LoadCashHistoryFromDisk()
    {
        try
        {
            if (File.Exists(CashHistoryFilePath))
            {
                string json = File.ReadAllText(CashHistoryFilePath);
                return JsonSerializer.Deserialize<List<FinanceWindow.CashSessionEntry>>(json) ?? new List<FinanceWindow.CashSessionEntry>();
            }
        }
        catch { /* игнорируем ошибки чтения */ }
        return new List<FinanceWindow.CashSessionEntry>();
    }

    private static void SaveCashHistoryToDisk(IEnumerable<FinanceWindow.CashSessionEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(CashHistoryFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(CashHistoryFilePath, json);
        }
        catch { /* игнорируем ошибки записи */ }
    }

    public class WarehouseItemVm : INotifyPropertyChanged
    {
        private string _id = "";
        private string _productName = "";
        private string _code = "";
        private string _article = "";
        private string _unit = "";
        private decimal _price;
        private decimal _discount;
        private double _stockQuantity;
        private Brush _stockBrush = Brushes.White;

        public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
        public string ProductName { get => _productName; set { _productName = value; OnPropertyChanged(); } }
        public string Code { get => _code; set { _code = value; OnPropertyChanged(); } }
        public string Article { get => _article; set { _article = value; OnPropertyChanged(); } }
        public string Unit { get => _unit; set { _unit = value; OnPropertyChanged(); } }
        public decimal Price { get => _price; set { _price = value; OnPropertyChanged(); } }
        public decimal Discount { get => _discount; set { _discount = value; OnPropertyChanged(); } }
        public double StockQuantity { get => _stockQuantity; set { _stockQuantity = value; OnPropertyChanged(); } }
        public Brush StockBrush { get => _stockBrush; set { _stockBrush = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
