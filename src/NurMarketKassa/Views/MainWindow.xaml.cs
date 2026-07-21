using MediatR;
using NurMarketKassa.Configuration;
using NurMarketKassa.Core;
using NurMarketKassa.Core.Application.Commands;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Interfaces;
using NurMarketKassa.Models;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Hardware;
using NurMarketKassa.ViewModels;
using NurMarketKassa.ViewModels.Catalog;
using NurMarketKassa.ViewModels.Scanning;
using NurMarketKassa.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;


namespace NurMarketKassa.Views;

public partial class MainWindow : Window, INotifyPropertyChanged
{
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

    public ICollectionView CatalogTableSource =>
    CollectionViewSource.GetDefaultView(
        new ObservableCollection<CatalogProductTileVm>(_allTilesKg.Concat(_allTilesPiece)));
    public ObservableCollection<WarehousePreset> WarehousePresets { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<string> Brands { get; } = new();
    private readonly List<CatalogProductTileVm> _allTilesKg = new();
    private readonly List<CatalogProductTileVm> _allTilesPiece = new();
    private readonly List<CatalogProductTileVm> _allSearchTiles = new();
    private readonly BulkObservableCollection<CatalogProductTileVm> _tilesAll = new();
    private readonly BulkObservableCollection<CatalogProductTileVm> _tilesKg = new();
    private readonly BulkObservableCollection<CatalogProductTileVm> _tilesPiece = new();
    private readonly BulkObservableCollection<CatalogProductTileVm> _searchTiles = new();
    private readonly ProductThumbService _catalogThumbService = new();
    private readonly IWeightScaleService _weightScale;
    private readonly IReceiptPrinterService _receiptPrinter;
    private DispatcherTimer? _searchDebounceTimer;
    private DispatcherTimer? _scaleUiTimer;
    private DispatcherTimer? _toastTimer;
    private FilterCriteria? _currentFilter;
    private const int AllProductsPageSize = 100;
    private const int CatalogPageSize = 50;
    private int _catalogVisibleLimitKg = CatalogPageSize;
    private int _catalogVisibleLimitPiece = CatalogPageSize;
    private bool _allProductsHasMore;
    private bool _allProductsLoading;
    private string _pendingSearchQuery = "";
    private string _catalogSearchFilter = "";
    private int _searchOffset;
    private bool _searchHasMore;
    private bool _isUiBusy;
    private bool _allowMainWindowClose;
    private bool _logoutNavigateScheduled;
    private readonly CancellationTokenSource _windowCts = new();
    private bool _catalogLoadBusy;
    private bool _catalogUpdateOverlayBusy;
    private Storyboard? _catalogRefreshSpinStoryboard;
    private Brush? _refreshIconDefaultBrush;
    private decimal? _shiftCashBalance;
    private bool _isMenuOpen;
    private bool _toolsPanelVisible = true;
    private bool _moreCartActionsVisible;
    private readonly IUserPrompts _userPrompts;

    /// <summary>null — активен основной чек; иначе id отложенного чека в памяти.</summary>
    private string? _viewingDeferredEntryId;
    /// <summary>Вкладка чека, на которой кассир был до текущего переключения (null = «Основной чек»).</summary>
    private string? _receiptReturnTargetId;
    private bool _appInitialized;
    private OpenReceiptSnapshot _primaryReceiptSnapshot = new();
    private bool _receiptSwitchBusy;
    private bool _deferCartBusy;
    private readonly NurMarketApiClient _apiClient;
    private readonly IBarcodeInputService _barcodeInputService;
    private readonly IMediator _mediator;
    private readonly IPosSessionService _posSessionService;
    private readonly IShiftStateService _shiftStateService;
    private readonly ICashShiftService _cashShiftService;
    private readonly ScaleWeightProvider _scaleWeightProvider;
    private readonly ProductSearchService _productSearchService;
    private readonly ICartService _cart;
    private readonly CatalogViewModel _catalogViewModel;
    private readonly BarcodeScanViewModel _barcodeScanViewModel;
    public ShiftViewModel ShiftViewModel { get; private set; }

    public CatalogViewModel Catalog => _catalogViewModel;
    public BarcodeScanViewModel BarcodeScan => _barcodeScanViewModel;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    private bool HasActiveCartLines =>
        _cart.HasCart && CartLines.Count > 0 && (UseSnapshotEditing || _cart.CanRefresh || _cart.IsLocalOffline);

    private bool HasCheckoutableCart()
    {
        if (OfflineModeHelper.CanOperateWithoutServer || _cart.IsLocalOffline)
            return LocalCartService.HasItems(_cart);

        return _cart.HasCart
               && CartLines.Count > 0
               && (_cart.CanRefresh || _cart.IsStaging);
    }

    private bool IsViewingDeferredReceipt => !string.IsNullOrEmpty(_viewingDeferredEntryId);

    /// <summary>Локальное редактирование снимка (отложенный чек или новый чек после откладывания).</summary>
    private bool UseSnapshotEditing =>
        !OfflineModeHelper.UseLocalOperations && (IsViewingDeferredReceipt || _cart.IsStaging);

    private HashSet<string> GetDeferredServerCartIds() =>
        DeferredCartsStore.LoadAll()
            .Select(x => x.ServerCartId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void LogReceiptOpenDiagnostics()
    {
        var items = _cart.HasCart
            ? string.Join(",", CartDisplayHelper.EnumerateItems(_cart.Root)
                .Select(it => CartDisplayHelper.TryItemId(it))
                .Where(id => !string.IsNullOrEmpty(id)))
            : "";
        PosLogger.Log(
            $"CurrentReceiptId={_cart.CartId ?? "—"} DeferredEntry={_viewingDeferredEntryId ?? "primary"} CartItems=[{items}]",
            "RECEIPT");
    }

    private void LogCartOpDiagnostics(string tag, string itemId)
    {
        var contains = ReceiptSnapshotCartEditor.ContainsItemId(_cart, itemId);
        PosLogger.Log($"ItemId={itemId} CartContains={contains} CartId={_cart.CartId}", tag);
    }

    private void AfterDeferredCartMutation()
    {
        PersistCurrentReceiptSnapshot();
        RebindCartUi();
        UpdateDeferredCartUi();
    }

    private async Task EnsureDeferredCartReadyForCheckoutAsync()
    {
        if (!IsViewingDeferredReceipt || App.Api == null)
            return;

        var entry = DeferredCartsStore.TryGetById(_viewingDeferredEntryId!);
        if (entry == null)
            return;

        PersistCurrentReceiptSnapshot();

        var cb = await EnsurePosCashboxIdAsync().ConfigureAwait(true);
        await StagingCartService
            .MaterializeSnapshotOnServerAsync(App.SalesApi, _cart, cb, _windowCts.Token)
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(_cart.CartId) || _cart.IsStaging)
            throw new ApiException("Не удалось подготовить отложенный чек к оплате на сервере.", 409);

        entry.ServerCartId = _cart.CartId;
        entry.CartJson = _cart.Root.GetRawText();
        DeferredCartsStore.UpdateEntry(entry);
        SyncActiveReceiptContext();
    }

    private async Task EnsureActiveCartReadyForCheckoutAsync()
    {
        if (App.Api == null || !_cart.IsStaging)
            return;

        var cb = await EnsurePosCashboxIdAsync().ConfigureAwait(true);
        await StagingCartService
            .MaterializeSnapshotOnServerAsync(App.SalesApi, _cart, cb, _windowCts.Token)
            .ConfigureAwait(true);
    }

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

    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
    nameof(IsLoading),
    typeof(bool),
    typeof(MainWindow),
    new PropertyMetadata(false));

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public MainWindow(
        IUserPrompts userPrompts,
        NurMarketApiClient apiClient,
        IBarcodeInputService barcodeInputService,
        IMediator mediator,
        IPosSessionService posSessionService,
        IShiftStateService shiftStateService,
        ICashShiftService cashShiftService,
        ScaleWeightProvider scaleWeightProvider,
        ProductSearchService productSearchService,
        IWeightScaleService weightScale,
        IReceiptPrinterService receiptPrinter,
        ICartService cart,
        CatalogViewModel catalogViewModel,
        BarcodeScanViewModel barcodeScanViewModel)
    {
        _cart = cart;
        _catalogViewModel = catalogViewModel;
        _barcodeScanViewModel = barcodeScanViewModel;
        _userPrompts = userPrompts;
        _apiClient = apiClient;
        _barcodeInputService = barcodeInputService;
        _mediator = mediator;
        _posSessionService = posSessionService;
        _shiftStateService = shiftStateService;
        _cashShiftService = cashShiftService;
        _scaleWeightProvider = scaleWeightProvider;
        _productSearchService = productSearchService;
        _weightScale = weightScale;
        _receiptPrinter = receiptPrinter;

        InitializeComponent();
        InjectLayoutMetricsDefaults();
        DataContext = this;

        ShiftViewModel = new ShiftViewModel(
        async () => await OpenShiftAsync(),
        async () => await CloseShiftAsync(),
        () => UserTitleText?.Text ?? "Кассир",
        () => GetCurrentBalance());

        if (FindName("IconText") is TextBlock iconText)
        {
            iconText.Text = _toolsPanelVisible ? "\uE70D" : "\uE70E";
        }

        CatalogItemsAll.ItemsSource = _tilesAll;
        CatalogItemsKg.ItemsSource = _tilesKg;
        CatalogItemsPiece.ItemsSource = _tilesPiece;
        CatalogSearchItems.ItemsSource = _searchTiles;
        if (CatalogTabs != null)
            CatalogTabs.SelectedIndex = 0;
        UpdateCatalogTabVisibility();
        UpdateThemeButtonIcon();
        RebuildReceiptTabStrip();

        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(App.Settings.Catalog.SearchDebounceMs, 120, 2000)),
        };
        _searchDebounceTimer.Tick += SearchDebounce_Tick;

        UserTitleText.Text = TryUserLabel(_apiClient.UserPayload);
        //BranchText.Text = FormatBranchLine(api.ActiveBranchId);
        App.OfflineSync.StateChanged += OfflineSync_StateChanged;
        App.CatalogBackgroundSync.UpdateAvailable += OnCatalogUpdateAvailable;
        App.CatalogBackgroundSync.ButtonStateChanged += OnCatalogButtonStateChanged;
        _shiftStateService.StateRefreshed += OnShiftStateRefreshed;
        _barcodeInputService.BarcodeScanned += OnBarcodeScanned;
        _catalogViewModel.LoadCompleted += OnCatalogLoadCompleted;
        _barcodeScanViewModel.ProductFound += OnBarcodeProductFound;
        RebindCartUi();
        UpdateShiftBanner();
        UpdateOfflineModeUi();
        ApplyFullscreenPreference();
        WireVirtualKeyboardIntegration();
    }

    private void WireVirtualKeyboardIntegration()
    {
        VirtualKeyboardBarcodeHandler.Configure(CatalogSearchBox, BarcodeBox);
        VirtualKeyboardBarcodeHandler.ProcessBarcodeAsync = ProcessKeyboardBarcodeAsync;
    }

    internal void ShowVirtualKeyboard() => FrmKeyboard.ShowKeyboard(this);

    private async void CatalogSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await ProcessProductLookupAsync(CatalogSearchBox, CatalogSearchBox.Text).ConfigureAwait(true);
    }

    private async void CatalogSearchItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not CatalogProductTileVm vm)
            return;

        e.Handled = true;
        SearchOverlayPanel.Visibility = Visibility.Collapsed;
        CatalogSearchBox.Text = "";
        await PickProductFromCatalogAsync(vm).ConfigureAwait(true);
    }

    private bool _keyboardBarcodeBusy;

    private Task ProcessKeyboardBarcodeAsync(TextBox searchTextBox, string query) =>
        ProcessProductLookupAsync(searchTextBox, query);

    private async Task ProcessProductLookupAsync(TextBox? sourceBox, string rawQuery)
    {
        if (_keyboardBarcodeBusy || _windowCts.IsCancellationRequested)
            return;

        var query = ProductSearchService.NormalizeLookupQuery(rawQuery);
        if (query.Length == 0)
            return;

        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return;
        if (!await EnsureSaleSessionReadyAsync().ConfigureAwait(true))
            return;

        _keyboardBarcodeBusy = true;
        try
        {
            var lookup = await _productSearchService
                .LookupProductsAsync(query, cancellationToken: _windowCts.Token)
                .ConfigureAwait(true);

            if (lookup.Items.Count == 0)
            {
                await RunOnUiAsync(() => ShowProductNotFound(query, sourceBox)).ConfigureAwait(true);
                return;
            }

            if (lookup.BestMatch != null)
            {
                await RunOnUiAsync(async () =>
                {
                    await AddProductToActiveCartAsync(lookup.BestMatch.Tile, useWeighDialogForWeight: false)
                        .ConfigureAwait(true);
                    ClearLookupFields(sourceBox);
                }).ConfigureAwait(true);
                return;
            }

            await RunOnUiAsync(() => ShowProductLookupOverlay(query, lookup.Items)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Окно закрыто — без уведомления.
        }
        finally
        {
            _keyboardBarcodeBusy = false;
        }
    }

    private void ShowProductNotFound(string query, TextBox? sourceBox)
    {
        PosLogger.Log($"[PRODUCT SEARCH] not found: \"{query}\"", "SEARCH");

        if (sourceBox != null)
        {
            ShowKeyboardBarcodeError("У вас нет такого товара в базе.", sourceBox);
            return;
        }

        CartMessageText.Text = "У вас нет такого товара в базе.";
        CartMessageText.Foreground = UiWarn;
    }

    private void ClearLookupFields(TextBox? sourceBox)
    {
        sourceBox?.Clear();
        if (sourceBox == CatalogSearchBox || sourceBox == null)
        {
            CatalogSearchBox.Text = "";
            SearchOverlayPanel.Visibility = Visibility.Collapsed;
        }

        if (sourceBox == BarcodeBox || sourceBox == null)
            BarcodeBox.Clear();

        sourceBox?.Focus();
        if (sourceBox != null)
            Keyboard.Focus(sourceBox);
    }

    private void ShowProductLookupOverlay(string query, IReadOnlyList<ProductLookupItem> items)
    {
        _pendingSearchQuery = query;
        _allSearchTiles.Clear();
        foreach (var item in items)
            _allSearchTiles.Add(item.Tile);

        ApplySearchViewport();
        SearchOverlayPanel.Visibility = Visibility.Visible;
        PosLogger.Log($"[PRODUCT SEARCH] showing picker with {items.Count} items", "SEARCH");
    }

    private void ShowKeyboardBarcodeError(string message, TextBox refocusBox)
    {
        PosMessageBox.Show(
            this,
            message,
            "Внимание",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        refocusBox.Focus();
        Keyboard.Focus(refocusBox);
    }

    private void OpenWarehousePageInBrowser()
    {
        const string url = "https://nurcrm.kg/crm/sklad";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowToast($"Не удалось открыть сайт: {ex.Message}", warn: true);
        }
    }

    private static CatalogProductTileVm? FindCatalogTileByBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return null;

        var code = barcode.Trim();
        return CatalogCacheService.Products
            .FirstOrDefault(p => string.Equals(p.Barcode?.Trim(), code, StringComparison.Ordinal));
    }

    private static CatalogProductTileVm? FindCatalogTile(string productId, string? barcode = null)
    {
        var tile = CatalogCacheService.Products
            .FirstOrDefault(p => string.Equals(p.Id, productId, StringComparison.OrdinalIgnoreCase));
        if (tile != null)
            return tile;

        return string.IsNullOrWhiteSpace(barcode) ? null : FindCatalogTileByBarcode(barcode);
    }

    private static double ResolveDisplayedStock(string productId, string? barcode, double? dbQuantity)
    {
        var tile = FindCatalogTile(productId, barcode);
        if (tile != null)
            return Convert.ToDouble(tile.Quantity);

        return Convert.ToDouble(dbQuantity ?? 0);
    }

    private void ShowNoStockBlocked(string productName, string productId, TextBox? refocusBox = null)
    {
        var warehouse = StockAvailabilityService.GetWarehouseQuantity(productId);
        var reserved = StockAvailabilityService.CalculateReservedQuantity(productId, _viewingDeferredEntryId);
        var available = StockAvailabilityService.GetAvailableToAdd(productId, _cart, _viewingDeferredEntryId);
        var reservedElsewhere = reserved > 1e-6 || (warehouse > 1e-6 && available <= 1e-6);
        var dialog = new NoStockDialog(productName, available, reservedElsewhere) { Owner = this };
        dialog.ShowDialog();

        if (dialog.GoToSite)
            OpenWarehousePageInBrowser();

        if (refocusBox == null)
            return;

        refocusBox.Clear();
        refocusBox.Focus();
        Keyboard.Focus(refocusBox);
    }

    private bool TryBlockCheckoutForStockIssues()
    {
        var issues = StockAvailabilityService.EvaluateCurrentCart(_cart, _viewingDeferredEntryId);
        if (issues.Count == 0)
            return false;

        var dialog = new PaymentStockBlockedDialog(issues) { Owner = this };
        dialog.ShowDialog();
        return true;
    }

    private void ValidateDeferredStockOnRestore()
    {
        var issues = StockAvailabilityService.EvaluateCurrentCart(_cart, _viewingDeferredEntryId);
        if (issues.Count == 0)
            return;

        var dialog = new DeferredStockIssuesDialog(issues) { Owner = this };
        dialog.ShowDialog();
    }

    private static string FormatStockQty(double value) =>
        value % 1 < 1e-6
            ? Math.Round(value, 0).ToString(CultureInfo.InvariantCulture)
            : CartDisplayHelper.FormatWeightQuantity(value);

    private void OnShiftStateRefreshed(ShiftStateSnapshot snapshot)
    {
        _shiftCashBalance = snapshot.CashBalance;
        if (Dispatcher.CheckAccess())
            UpdateShiftBanner();
        else
            Dispatcher.Invoke(UpdateShiftBanner);
    }

    private void OnBarcodeScanned(string barcode)
    {
        if (Application.Current.Windows.OfType<WarehouseWindow>().Any(w => w.IsVisible))
            return;

        _ = ProcessBarcodeScanAsync(barcode);
    }

    private async Task ProcessBarcodeScanAsync(string barcode)
    {
        if (_windowCts.IsCancellationRequested)
            return;

        try
        {
            if (!await _barcodeScanViewModel.ProcessBarcodeScanAsync(barcode, _windowCts.Token).ConfigureAwait(true))
            {
                var message = _barcodeScanViewModel.ScanErrorMessage;
                if (!string.IsNullOrWhiteSpace(message))
                    await RunOnUiAsync(() => ShowBarcodeScanError(message)).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Окно закрыто или скан отменён — не показываем ошибку.
        }
    }

    private void OnBarcodeProductFound(ScannedProductFoundEventArgs args)
    {
        _ = RunOnUiAsync(async () =>
        {
            var tile = FindCatalogTile(args.Product.Id, args.Product.Barcode);
            if (tile == null)
            {
                ShowBarcodeScanError("У вас нет такого товара в базе.");
                return;
            }

            if (args.WeightKg is > 0)
            {
                await AddProductToActiveCartAsync(
                    tile,
                    useWeighDialogForWeight: false,
                    presetQuantity: (double)args.WeightKg.Value).ConfigureAwait(true);
                return;
            }

            await AddProductToActiveCartAsync(
                tile,
                useWeighDialogForWeight: tile.MustWeigh).ConfigureAwait(true);
        });
    }

    private void ShowBarcodeScanError(string message)
    {
        CartMessageText.Text = message;
        CartMessageText.Foreground = UiWarn;
        BarcodeBox.Clear();
        BarcodeBox.Focus();
        Keyboard.Focus(BarcodeBox);
    }

    private void RunOnUi(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.Invoke(action);
    }

    private Task RunOnUiAsync(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
    }

    private Task RunOnUiAsync(Func<Task> action)
    {
        if (Dispatcher.CheckAccess())
            return action();

        return Dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task.Unwrap();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_appInitialized)
            return;

        await InitializeApplicationAsync(null, _windowCts.Token).ConfigureAwait(true);
    }

    public async Task InitializeApplicationAsync(IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        if (_appInitialized)
            return;

        progress?.Report("Загрузка кассы...");

        if (AccountCatalogIsolation.RequireForcedCatalogSync)
            ClearCatalogViewport();

        bool dbLoaded = false;
        if (!AccountCatalogIsolation.RequireForcedCatalogSync
            && CatalogCacheService.Products.Count == 0)
            dbLoaded = CatalogCacheService.LoadFromDatabase();

        if (CatalogCacheService.Products.Count > 0)
        {
            RestoreTilesFromCache();
            UpdateCacheStatus();
            if (dbLoaded && IsLoaded)
                ShowToast("Каталог загружен из локальной базы", false);
        }

        progress?.Report("Загрузка каталога...");
        try
        {
            await Task.WhenAll(
                    LoadProfileHeaderAsync(cancellationToken),
                    RefreshShiftStateAsync(cancellationToken),
                    CompanyInfoService.RefreshAsync(App.AuthApi, cancellationToken))
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await EnsureSaleSessionReadyAsync(silent: true, cancellationToken: cancellationToken).ConfigureAwait(true);
            CapturePrimaryReceiptSnapshot();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!OfflineModeHelper.UseLocalOperations)
        {
            if (AccountCatalogIsolation.RequireForcedCatalogSync
                || CatalogCacheService.Products.Count == 0
                || progress != null)
            {
                progress?.Report("Синхронизация каталога...");
                await LoadCatalogAsync(cancellationToken, manual: true).ConfigureAwait(true);
                AccountCatalogIsolation.ClearForcedCatalogSyncFlag();
            }
            else
                _ = LoadCatalogAsync(cancellationToken);
        }
        else if (CatalogCacheService.Products.Count == 0)
        {
            TryRestoreFromCache();
            AccountCatalogIsolation.ClearForcedCatalogSyncFlag();
        }

        progress?.Report("Подключение оборудования...");
        progress?.Report("Проверка весов...");
        StartScaleMonitoring();
        UpdateSystemStatusLine();

        progress?.Report("Проверка принтера...");
        progress?.Report("Подготовка рабочего места...");

        if (!string.IsNullOrWhiteSpace(App.OfflineBootstrapMessage) && IsLoaded)
            ShowToast(App.OfflineBootstrapMessage, warn: true);

        var queuePending = OfflinePendingSalesStore.PendingCount + OfflinePendingSalesStore.FailedCount;
        if (queuePending > 0 && IsLoaded)
            ShowToast($"Несинхронизированных чеков: {queuePending}. Синхронизация начнётся при появлении связи.", warn: queuePending > 0);

        _ = App.OfflineSync.ProbeNowAsync(cancellationToken);
        if (!OfflineModeHelper.UseLocalOperations)
            _ = App.CatalogBackgroundSync.CheckNowAsync(cancellationToken);

        if (IsLoaded)
        {
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

        _appInitialized = true;
    }

    public void ClearCatalogViewport()
    {
        _allTilesKg.Clear();
        _allTilesPiece.Clear();
        _tilesKg.Clear();
        _tilesPiece.Clear();
        _tilesAll.Clear();
        _allSearchTiles.Clear();
        _searchTiles.Clear();
        ResetAllProductsPage();
        OnPropertyChanged(nameof(CatalogTableSource));
        UpdateCatalogCount();
    }

    private void RestoreTilesFromCache()
    {
        _allTilesKg.Clear();
        _allTilesPiece.Clear();
        _tilesKg.Clear();
        _tilesPiece.Clear();
        ResetAllProductsPage();

        CatalogProductClassifier.SplitIntoCatalogLists(CatalogCacheService.Products, _allTilesKg, _allTilesPiece);
        SortByFavorite();
        RefreshCategoriesAndBrands();
        ApplyCatalogViewport();
        OnPropertyChanged(nameof(CatalogTableSource));

        if (GetSelectedCatalogTab() == CatalogTabKind.All)
            _ = LoadAllProductsPageAsync(append: false);
    }

    private void ResetAllProductsPage()
    {
        _tilesAll.Clear();
        _allProductsHasMore = false;
    }

    private enum CatalogTabKind
    {
        All = 0,
        Weight = 1,
        Piece = 2,
    }

    private CatalogTabKind GetSelectedCatalogTab() =>
        CatalogTabs?.SelectedIndex switch
        {
            1 => CatalogTabKind.Weight,
            2 => CatalogTabKind.Piece,
            _ => CatalogTabKind.All,
        };

    private int GetActiveCatalogPageSize() =>
        GetSelectedCatalogTab() == CatalogTabKind.All ? AllProductsPageSize : CatalogPageSize;

    public void UpdateCacheStatus() => UpdateSystemStatusLine();

    public void UpdateSystemStatusLine()
    {
        if (SystemStatusText == null)
            return;

        var line = CashierStatusLineBuilder.Build(_weightScale);
        SystemStatusText.Text = line.Text;
        SystemStatusText.ToolTip = line.ToolTip;
    }

    private void CatalogGrid_Loaded(object sender, RoutedEventArgs e) => AdjustCatalogGridColumns();
    private void CatalogGrid_SizeChanged(object sender, SizeChangedEventArgs e) => AdjustCatalogGridColumns();

    private void AdjustCatalogGridColumns()
    {
        if (CatalogGrid == null || CatalogGrid.Columns.Count == 0)
            return;

        double[] proportions = { 5, 1.5, 1, 2, 1, 1 }; // Название:Цена:Остаток:Штрихкод:Весовой:★
        double totalProportion = proportions.Sum();

        double availableWidth = CatalogGrid.ActualWidth - SystemParameters.VerticalScrollBarWidth - 12;
        if (availableWidth <= 0)
            return;

        for (int i = 0; i < proportions.Length && i < CatalogGrid.Columns.Count; i++)
        {
            CatalogGrid.Columns[i].Width = availableWidth * (proportions[i] / totalProportion);
        }
    }

    private async Task LoadProfileHeaderAsync(CancellationToken ct)
    {
        ProfileStatusText.Text = "Загрузка профиля…";
        ProfileStatusText.Foreground = UiMuted;

        try
        {
            var profile = await App.AuthApi.GetProfileAsync(ct).ConfigureAwait(true);
            if (profile.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                ProfileStatusText.Text = "Данные профиля не получены.";
                ProfileStatusText.Foreground = UiWarn;
                return;
            }

            App.AuthApi.ApplyBranchFromProfile(profile);
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

    public async Task OpenShiftAsync()
    {
        var dlg = new OpenShiftDialog { SuggestedBalance = _shiftCashBalance };
        if (PosDialogHost.Show(dlg, this) != true)
            return;

        await OpenShiftWithCashAsync(dlg.OpeningCash).ConfigureAwait(true);
    }

    public async Task CloseShiftAsync()
    {
        if (string.IsNullOrEmpty(App.ActiveShiftId))
            return;

        var dlg = new CloseShiftDialog { SuggestedBalance = _shiftCashBalance };
        if (PosDialogHost.Show(dlg, this) != true)
            return;

        await CloseShiftWithCashAsync(dlg.ClosingCash).ConfigureAwait(true);
    }

    private void RefreshCategoriesAndBrands()
    {
        _catalogViewModel.RefreshCategoriesAndBrands();
        Categories.Clear();
        foreach (var category in _catalogViewModel.Categories)
            Categories.Add(category.Name);

        var brands = new HashSet<string>();
        foreach (var vm in _allTilesKg.Concat(_allTilesPiece))
        {
            if (!string.IsNullOrWhiteSpace(vm.Brand))
                brands.Add(vm.Brand);
        }

        Brands.Clear();
        foreach (var b in brands.OrderBy(x => x))
            Brands.Add(b);
    }

    private void OnCatalogLoadCompleted(CatalogLoadCompletedEventArgs args)
    {
        Dispatcher.Invoke(() =>
        {
            if (args.RestoredFromCache && args.Success)
            {
                RestoreTilesFromCache();
                ShowToast($"Офлайн: каталог из локальной БД ({args.ProductCount} шт.)", warn: false);
                return;
            }

            if (!args.Success)
            {
                if (!string.IsNullOrWhiteSpace(args.ErrorMessage))
                    ShowToast(args.ErrorMessage, warn: true);
                SetCatalogRefreshVisual(CatalogSyncButtonState.Error);
                return;
            }

            ApplyCatalogDataToViewport();
            App.CatalogBackgroundSync.ClearPendingUpdate();
            HideCatalogUpdateOverlay();
            SetCatalogRefreshVisual(CatalogSyncButtonState.Idle);

            if (args.ManualRefresh || args.Added > 0 || args.Changed > 0 || args.Deleted > 0)
            {
                ShowToast(
                    $"Каталог обновлен.\nДобавлено: {args.Added}\nИзменено: {args.Changed}\nУдалено: {args.Deleted}",
                    warn: false);
            }
        });
    }

    public decimal GetCurrentBalance() => _shiftCashBalance ?? 0m;

    /// <summary>Начальные значения DynamicResource для плиток каталога и строк корзины.</summary>
    private void InjectLayoutMetricsDefaults()
    {
        void Put(string key, double v) => Resources[key] = v;
        Put("CatalogTileWidth", 175);
        Put("CatalogTileMinHeight", 145);
        Resources["CatalogTileSize"] = new Size(175, 145);
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
        const double tileW = 175;
        const double tileH = 145;
        Resources["CatalogTileWidth"] = tileW;
        Resources["CatalogTileMinHeight"] = tileH;
        Resources["CatalogTileSize"] = new Size(tileW, tileH);
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
        // Обычное закрытие (не принудительное)
        if (!App.ExitWithoutLoginRedirect && !_allowMainWindowClose)
        {
            // ► Проверка открытой смены
            if (!string.IsNullOrEmpty(App.ActiveShiftId))
            {
                e.Cancel = true;
                var shiftResult = ShiftNotClosedDialog.Show(this);

                if (shiftResult == ShiftNotClosedDialogResult.Cancel)
                    return;

                if (shiftResult == ShiftNotClosedDialogResult.CloseShift)
                {
                    try
                    {
                        CloseShiftAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) { }
                    catch { /* ошибка сервера не должна мешать выйти */ }
                }
            }

            // Стандартный переход на окно логина
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
                catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
                {
                    /* отмена фоновых задач при закрытии */
                }
                finally
                {
                    _logoutNavigateScheduled = false;
                }
            }), DispatcherPriority.Normal);
            return;
        }

        // Очистка ресурсов при окончательном закрытии приложения
        _searchDebounceTimer?.Stop();
        _scaleUiTimer?.Stop();
        _toastTimer?.Stop();
        _barcodeInputService.BarcodeScanned -= OnBarcodeScanned;
        _barcodeScanViewModel.ProductFound -= OnBarcodeProductFound;

        try
        {
            _windowCts.Cancel();
        }
        catch (ObjectDisposedException) { }

        App.OfflineSync.StateChanged -= OfflineSync_StateChanged;
        App.CatalogBackgroundSync.UpdateAvailable -= OnCatalogUpdateAvailable;
        App.CatalogBackgroundSync.ButtonStateChanged -= OnCatalogButtonStateChanged;
        _catalogRefreshSpinStoryboard?.Stop();
        _scaleUiTimer?.Stop();
        _scaleUiTimer = null;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        try
        {
            _windowCts.Dispose();
        }
        catch { }
    }

    private void ToggleKeyboard_Click(object sender, RoutedEventArgs e)
    {
        if (FrmKeyboard.IsShown)
        {
            TouchKeyboard.Close();
            return;
        }

        VirtualKeyboardInput.RememberInputTarget(Keyboard.FocusedElement as IInputElement);
        FrmKeyboard.ShowKeyboard(this);
    }

    private void OfflineSync_StateChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateOfflineModeUi();
            UpdateDeferredCartUi();
            UpdateOfflineStatusButton();
            if (App.OfflineSync.IsOnline)
                _ = App.CatalogBackgroundSync.CheckNowAsync();
        });
    }

    private void UpdateOfflineModeUi()
    {
        var sync = App.OfflineSync;
        NetworkModeText.Text = sync.IsOnline && OfflinePendingSalesStore.PendingCount + OfflinePendingSalesStore.FailedCount == 0
            ? "Очередь синхронизации пуста"
            : sync.StatusText;
        NetworkModeText.Foreground = sync.IsOnline
            ? (sync.IsSyncInProgress ? UiWarn : UiOk)
            : UiWarn;
        UpdateOfflineStatusButton();
    }

    private void UpdateOfflineStatusButton()
    {
        if (OfflineStatusLabel == null) return;

        var sync = App.OfflineSync;
        int pending = OfflinePendingSalesStore.PendingCount + OfflinePendingSalesStore.FailedCount;

        if (OfflineStatusDot != null)
            OfflineStatusDot.Fill = sync.IsOnline ? UiOk : UiWarn;

        if (sync.IsSyncInProgress)
            OfflineStatusLabel.Text = $"Синхронизация… | Очередь: {pending}";
        else if (!sync.IsOnline)
            OfflineStatusLabel.Text = $"Оффлайн | Очередь: {pending}";
        else if (pending > 0)
            OfflineStatusLabel.Text = $"Онлайн | Очередь: {pending}";
        else
            OfflineStatusLabel.Text = "Онлайн | Очередь: 0";
    }

    private void UpdateScaleStatusLine() => UpdateSystemStatusLine();

    private void StartScaleMonitoring()
    {
        _scaleUiTimer?.Stop();
        _scaleUiTimer = null;

        if (HardwareModeHelper.UsePhysicalScale())
        {
            _weightScale.Start();
            _scaleUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _scaleUiTimer.Tick += (_, _) => UpdateScaleStatusLine();
            _scaleUiTimer.Start();
        }
        else if (HardwareModeHelper.UseVirtualScale(App.Settings))
        {
            _weightScale.Start();
            _scaleUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _scaleUiTimer.Tick += (_, _) => UpdateScaleStatusLine();
            _scaleUiTimer.Start();
        }
        else
        {
            _weightScale.Stop();
        }
    }

    private IWeightScaleService? GetScaleForWeighDialog() =>
        HardwareModeHelper.UsePhysicalScale() ? _weightScale : null;

    private async Task<bool> TryPrintReceiptAsync(
        string cartJson,
        string? offlineNote = null,
        string? paymentMethodKey = null,
        string? cashReceived = null,
        string? receiptText = null)
    {
        if (!HardwareModeHelper.UsePhysicalPrinter() && !HardwareModeHelper.UseVirtualPrinter(App.Settings))
            return false;

        return await _receiptPrinter.PrintReceiptAsync(
            new CartSnapshot
            {
                CartJson = cartJson,
                OfflineNote = offlineNote,
                PaymentMethodKey = paymentMethodKey,
                CashReceived = cashReceived,
                ReceiptText = receiptText,
            }).ConfigureAwait(true);
    }

    /// <summary>
    /// Печать чека после успешной оплаты: окно успеха → опциональная печать → новый чек.
    /// </summary>
    private async Task RunPostPaymentUiAsync(
        double totalAmount,
        string? paymentMethod,
        string? cashReceived,
        string cartJsonSnapshot,
        bool printReceipt,
        JsonElement? checkoutResponse = null,
        string? offlineNote = null)
    {
        var success = PosDialogs.ShowPaymentSuccess(this, totalAmount, printReceipt);
        if (success == null)
            return;

        if (success.PrintReceipt)
        {
            CartMessageText.Text = "Печать чека...";
            CartMessageText.Foreground = UiOk;
            await TryPrintReceiptSafeAsync(
                cartJsonSnapshot,
                offlineNote: offlineNote,
                paymentMethodKey: paymentMethod,
                cashReceived: cashReceived,
                checkoutResponse: checkoutResponse).ConfigureAwait(true);
        }

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
    }

    private async Task<bool> TryPrintReceiptSafeAsync(
        string cartJson,
        string? offlineNote = null,
        string? paymentMethodKey = null,
        string? cashReceived = null,
        JsonElement? checkoutResponse = null)
    {
        try
        {
            if (!HardwareModeHelper.IsPrinterPortConfigured())
            {
                PosLogger.Log("Печать пропущена: порт принтера не настроен.", "PRINTER");
                return false;
            }

            var receiptText = await ResolveReceiptTextAsync(
                checkoutResponse,
                cartJson,
                paymentMethodKey,
                cashReceived).ConfigureAwait(true);

            var printed = await TryPrintReceiptAsync(
                cartJson,
                offlineNote: offlineNote,
                paymentMethodKey: paymentMethodKey,
                cashReceived: cashReceived,
                receiptText: receiptText).ConfigureAwait(true);

            if (!printed)
                PosLogger.Log("Печать: служба принтера вернула false.", "PRINTER");

            return printed;
        }
        catch (Exception ex)
        {
            PosLogger.Log($"Печать: {ex.Message}\n{ex.StackTrace}", "PRINTER");
            return false;
        }
    }


    private async Task<string?> ResolveReceiptTextAsync(
        JsonElement? checkoutResponse,
        string cartJsonSnapshot,
        string? paymentMethod,
        string? cashReceived)
    {
        if (checkoutResponse is { } checkout)
        {
            var fromCheckout = CheckoutResponseHelper.TryReceiptTextFromCheckout(checkout);
            if (!string.IsNullOrWhiteSpace(fromCheckout))
            {
                PosLogger.Log($"Текст чека получен из ответа checkout ({fromCheckout.Length} симв.)", "PRINTER");
                return fromCheckout;
            }

            var saleId = CheckoutResponseHelper.TrySaleId(checkout);
            if (!string.IsNullOrEmpty(saleId))
            {
                try
                {
                    PosLogger.Log($"GET receipt для sale_id={saleId}", "PRINTER");
                    var rec = await App.SalesApi.PosSaleReceiptAsync(saleId).ConfigureAwait(true);
                    var fromApi = CheckoutResponseHelper.TryReceiptTextFromSaleReceiptPayload(rec);
                    if (!string.IsNullOrWhiteSpace(fromApi))
                    {
                        PosLogger.Log($"Текст чека получен из API ({fromApi.Length} симв.)", "PRINTER");
                        return fromApi;
                    }
                }
                catch (Exception ex)
                {
                    PosLogger.Log($"GET receipt: {ex.Message}", "PRINTER");
                }
            }
        }

        PosLogger.Log("Текст чека собран локально из снимка корзины.", "PRINTER");
        return CartReceiptTextBuilder.BuildSimpleReceipt(cartJsonSnapshot, null, paymentMethod, cashReceived);
    }

    private async Task<bool> EnsureSaleSessionReadyAsync(
        string pendingMessage = "Открываем новый чек…",
        string successMessage = "Новый чек открыт. Можно добавлять товары.",
        bool silent = false,
        CancellationToken cancellationToken = default)
    {
        // Смену при добавлении товара проверяем отдельно (всегда с диалогом) — см. PickProductFromCatalogAsync / RunScanAsync.
        if (_cart.CanRefresh || _cart.IsStaging || _cart.IsLocalOffline)
            return true;

        if (!await EnsureShiftReadyForOperationsAsync(silent, cancellationToken).ConfigureAwait(true))
            return false;

        if (OfflineModeHelper.CanOperateWithoutServer)
        {
            if (!silent)
            {
                CartMessageText.Text = pendingMessage;
                CartMessageText.Foreground = UiMuted;
            }

            LocalCartService.StartNewLocalCart(_cart);
            RebindCartUi();

            if (!silent)
            {
                CartMessageText.Text = "Офлайн-чек открыт. Можно добавлять товары.";
                CartMessageText.Foreground = UiOk;
            }

            return true;
        }

        if (!silent)
        {
            CartMessageText.Text = pendingMessage;
            CartMessageText.Foreground = UiMuted;
        }

        try
        {
            await TryStartNewSaleAsync(cancellationToken).ConfigureAwait(true);
            RebindCartUi();
            if (!_cart.CanRefresh && !_cart.IsStaging && !_cart.IsLocalOffline)
            {
                if (!silent)
                {
                    CartMessageText.Text = "Не удалось открыть новый чек. Повторите попытку или нажмите «Новый чек».";
                    CartMessageText.Foreground = UiWarn;
                }

                return false;
            }

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
            if (OfflineModeHelper.UseLocalOperations)
            {
                LocalCartService.StartNewLocalCart(_cart);
                RebindCartUi();
                return true;
            }

            if (!silent)
            {
                CartMessageText.Text = ex.Message;
                CartMessageText.Foreground = UiWarn;
            }

            return false;
        }
        catch (HttpRequestException ex)
        {
            if (OfflineModeHelper.UseLocalOperations || OfflineModeHelper.CanOperateWithoutServer)
            {
                LocalCartService.StartNewLocalCart(_cart);
                RebindCartUi();
                if (!silent)
                {
                    CartMessageText.Text = "Офлайн-чек открыт. Можно добавлять товары.";
                    CartMessageText.Foreground = UiOk;
                }

                return true;
            }

            if (!silent)
            {
                CartMessageText.Text = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
                CartMessageText.Foreground = UiWarn;
            }

            return false;
        }
    }

    private void CatalogView_Changed(object sender, RoutedEventArgs e)
    {
        // При переключении на таблицу гарантированно задаём ItemsSource,
        // чтобы DataGrid сразу показал актуальные данные.
        if (BtnTable?.IsChecked == true)
            CatalogGrid.ItemsSource = CatalogTableSource;
        // Для карточек ничего не делаем – ItemsControl обновляется автоматически.
    }

    private void UpdateCartStateUi()
    {
        CartItemsCountText.Text = CartLines.Count.ToString(CultureInfo.InvariantCulture);
        if (CartEmptyPanel != null)
            CartEmptyPanel.Visibility = CartLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RebuildReceiptTabStrip();

        if (!_cart.HasCart || (!_cart.CanRefresh && !_cart.IsStaging && !_cart.IsLocalOffline))
        {
            CartStateText.Text = string.IsNullOrEmpty(App.ActiveShiftId)
                ? "Откройте смену. После этого новый чек будет открываться автоматически."
                : "Новый чек откроется автоматически. Можно сразу сканировать товары.";
            return;
        }

        var positions = CartLines.Count;
        var total = CartDisplayHelper.TotalDue(_cart.Root);
        CartStateText.Text = positions == 0
            ? "Чек открыт. Можно сканировать товары."
            : $"Активный чек: {positions} поз. · {CartDisplayHelper.FormatMoney(total)} сом к оплате.";
    }

    private void UpdateDeferredCartUi()
    {
        var count = DeferredCartsStore.Count();

        DeferredCountText.Text = count.ToString(CultureInfo.InvariantCulture);

        RebuildReceiptTabStrip();
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

    private string BuildDeferredCartLabel()
    {
        var items = CartDisplayHelper.EnumerateItems(_cart.Root).ToList();
        var total = CartDisplayHelper.TotalDue(_cart.Root);
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

    private DeferredCartEntry SaveCurrentCartAsDeferred(string? label = null) =>
        SaveOrUpdateDeferredCart(label, existingId: null, snapshot: OpenReceiptSnapshot.Capture(_cart));

    private DeferredCartEntry SaveOrUpdateDeferredCart(
        string? label,
        string? existingId,
        OpenReceiptSnapshot? snapshot = null)
    {
        snapshot ??= OpenReceiptSnapshot.Capture(_cart);
        var cartJson = OpenReceiptSnapshot.CloneCartJson(snapshot.CartJson);

        if (!string.IsNullOrEmpty(existingId))
        {
            var existing = DeferredCartsStore.TryGetById(existingId);
            if (existing != null)
            {
                existing.Label = string.IsNullOrWhiteSpace(label) ? BuildDeferredCartLabel() : label.Trim();
                existing.CartJson = cartJson;
                existing.ServerCartId = null;
                existing.SavedAt = DateTimeOffset.Now;
                DeferredCartsStore.UpdateEntry(existing);
                UpdateDeferredCartUi();
                LogDeferredCartSeparation("DEFER update", existing.Id, existing.ServerCartId, cartJson, null);
                return existing;
            }
        }

        var entry = new DeferredCartEntry
        {
            Label = string.IsNullOrWhiteSpace(label) ? BuildDeferredCartLabel() : label.Trim(),
            CartJson = cartJson,
            ServerCartId = null,
        };
        DeferredCartsStore.Add(entry);
        UpdateDeferredCartUi();
        LogDeferredCartSeparation("DEFER save", entry.Id, entry.ServerCartId, cartJson, null);
        return entry;
    }

    private void LogDeferredCartSeparation(
        string phase,
        string deferredEntryId,
        string? deferredServerCartId,
        string deferredCartJson,
        string? previousCartId)
    {
        var deferredLines = OpenReceiptSnapshot.CountLines(deferredCartJson);
        var deferredHash = OpenReceiptSnapshot.ComputeItemsSignatureHash(deferredCartJson);
        var primaryLines = OpenReceiptSnapshot.CountLines(_primaryReceiptSnapshot.CartJson);
        var primaryHash = OpenReceiptSnapshot.ComputeItemsSignatureHash(_primaryReceiptSnapshot.CartJson);
        var activeLines = _cart.HasCart
            ? CartDisplayHelper.EnumerateItems(_cart.Root).Count()
            : 0;
        var activeHash = _cart.HasCart
            ? OpenReceiptSnapshot.ComputeItemsSignatureHash(_cart.Root.GetRawText())
            : 0;
        var cartLinesHash = string.Join(";", CartLines.Select(r => $"{r.ItemId}:{r.ProductId}:{r.Qty}"))
            .GetHashCode(StringComparison.Ordinal);

        PosLogger.Log(
            $"{phase}: prevCartId={previousCartId ?? "—"} deferredEntryId={deferredEntryId} deferredServerCartId={deferredServerCartId ?? "—"} " +
            $"newActiveCartId={_cart.CartId ?? "—"} deferredLines={deferredLines} deferredHash={deferredHash} " +
            $"primarySnapshotLines={primaryLines} primarySnapshotHash={primaryHash} " +
            $"activeCartLines={activeLines} activeCartHash={activeHash} cartLinesUi={CartLines.Count} cartLinesHash={cartLinesHash}",
            "DEFER");
    }

    private void ResetPrimaryReceiptSnapshotForNewActiveReceipt()
    {
        _primaryReceiptSnapshot = _cart.HasCart
            ? OpenReceiptSnapshot.Capture(_cart)
            : OpenReceiptSnapshot.CreateEmpty();

        PosLogger.Log(
            $"DEFER primary reset: activeCartId={_cart.CartId ?? "—"} " +
            $"primaryLines={OpenReceiptSnapshot.CountLines(_primaryReceiptSnapshot.CartJson)} " +
            $"primaryHash={OpenReceiptSnapshot.ComputeItemsSignatureHash(_primaryReceiptSnapshot.CartJson)}",
            "DEFER");
    }

    private async Task<bool> DeferCurrentCartAsync(bool startNewSale = true, bool showToast = true, string? label = null)
    {
        if (_deferCartBusy)
            return false;

        if (!HasActiveCartLines)
        {
            if (showToast)
                ShowToast("Корзина пуста — нечего откладывать.", warn: true);
            return false;
        }

        _deferCartBusy = true;
        try
        {
            if (startNewSale && !await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
                return false;

            var previousCartId = _cart.CartId;
            var replacingDeferredId = _viewingDeferredEntryId;

            if (IsViewingDeferredReceipt)
                PersistCurrentReceiptSnapshot();

            var deferredSnapshot = OpenReceiptSnapshot.Capture(_cart);
            var entry = SaveOrUpdateDeferredCart(label, replacingDeferredId, deferredSnapshot);

            _viewingDeferredEntryId = null;
            _primaryReceiptSnapshot = OpenReceiptSnapshot.CreateEmpty();

            if (showToast)
                ShowToast($"Отложено: «{entry.Label}».");

            PosLogger.Log($"RECEIPT defer: id={entry.Id}, startNewSale={startNewSale}", "RECEIPT");
            if (startNewSale)
                await PrepareFreshSaleAfterDeferAsync(previousCartId, entry).ConfigureAwait(true);
            else
                LogDeferredCartSeparation("DEFER done", entry.Id, entry.ServerCartId, entry.CartJson, previousCartId);

            return true;
        }
        finally
        {
            _deferCartBusy = false;
        }
    }

    private async Task LoadCatalogAsync(CancellationToken cancellationToken = default, bool manual = false)
    {
        if (_catalogLoadBusy)
            return;

        _catalogLoadBusy = true;
        SetCatalogRefreshVisual(CatalogSyncButtonState.Syncing);
        if (RefreshCatalogButton != null)
            RefreshCatalogButton.IsEnabled = false;
        try
        {
            await _catalogViewModel.LoadCatalogAsync(cancellationToken, manual).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _catalogLoadBusy = false;
            if (RefreshCatalogButton != null)
                RefreshCatalogButton.IsEnabled = true;
            UpdateCatalogPagerUi();
        }
    }

    private void ApplyCatalogDataToViewport()
    {
        RestoreTilesFromCache();
        SortByFavorite();
        ResetCatalogViewport();
        RefreshCategoriesAndBrands();
        UpdateCacheStatus();
    }

    private void OnCatalogUpdateAvailable(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (OfflineModeHelper.UseLocalOperations || _catalogLoadBusy)
                return;
            ShowCatalogUpdateOverlay();
        });
    }

    private void OnCatalogButtonStateChanged(object? sender, CatalogSyncButtonState state)
    {
        Dispatcher.Invoke(() => SetCatalogRefreshVisual(state));
    }

    private void ShowCatalogUpdateOverlay()
    {
        SetCatalogUpdateOverlayBusyState(busy: false);
        if (CatalogUpdateOverlay != null)
            CatalogUpdateOverlay.Visibility = Visibility.Visible;
    }

    internal void HideCatalogUpdateOverlay()
    {
        if (CatalogUpdateOverlay != null)
            CatalogUpdateOverlay.Visibility = Visibility.Collapsed;
        SetCatalogUpdateOverlayBusyState(busy: false);
    }

    private void SetCatalogUpdateOverlayBusyState(bool busy)
    {
        if (CatalogUpdateNowButton != null)
        {
            CatalogUpdateNowButton.IsEnabled = !busy;
            CatalogUpdateNowButton.Content = busy ? "Подождите, идет изменение..." : "Обновить сейчас";
        }

        if (CatalogUpdateLaterButton != null)
            CatalogUpdateLaterButton.IsEnabled = !busy;
    }

    private async void CatalogUpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_catalogUpdateOverlayBusy)
            return;

        _catalogUpdateOverlayBusy = true;
        SetCatalogUpdateOverlayBusyState(busy: true);

        try
        {
            await CatalogUpdateNow_ClickCoreAsync().ConfigureAwait(true);
        }
        finally
        {
            _catalogUpdateOverlayBusy = false;
        }
    }

    private async Task CatalogUpdateNow_ClickCoreAsync()
    {
        if (OfflineModeHelper.UseLocalOperations)
        {
            TryRestoreFromCache();
            HideCatalogUpdateOverlay();
            return;
        }

        while (_catalogLoadBusy && !_windowCts.IsCancellationRequested)
            await Task.Delay(50, _windowCts.Token).ConfigureAwait(true);

        _catalogLoadBusy = true;
        SetCatalogRefreshVisual(CatalogSyncButtonState.Syncing);
        if (RefreshCatalogButton != null)
            RefreshCatalogButton.IsEnabled = false;

        try
        {
            var result = await Task.Run(
                async () => await CatalogCacheService.SyncCatalogFullAsync(_windowCts.Token).ConfigureAwait(false),
                _windowCts.Token).ConfigureAwait(true);

            if (!result.Success)
            {
                SetCatalogRefreshVisual(CatalogSyncButtonState.Error);
                if (!TryRestoreFromCache())
                    ShowToast(result.ErrorMessage ?? "Ошибка загрузки каталога.", warn: true);
                return;
            }

            ApplyCatalogDataToViewport();
            App.CatalogBackgroundSync.ClearPendingUpdate();
            SetCatalogRefreshVisual(CatalogSyncButtonState.Idle);
            ShowToast(
                $"Каталог обновлен.\nДобавлено: {result.Added}\nИзменено: {result.Changed}\nУдалено: {result.Deleted}",
                warn: false);
        }
        catch (TaskCanceledException)
        {
            if (!TryRestoreFromCache())
                ShowToast("Каталог: таймаут.", warn: true);
            SetCatalogRefreshVisual(CatalogSyncButtonState.Error);
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex)
        {
            if (!TryRestoreFromCache())
                ShowToast($"Каталог: {ex.Message}", warn: true);
            SetCatalogRefreshVisual(CatalogSyncButtonState.Error);
        }
        catch (HttpRequestException ex)
        {
            if (!TryRestoreFromCache())
                ShowToast(string.IsNullOrWhiteSpace(ex.Message)
                    ? "Каталог: нет сети."
                    : $"Каталог: {ex.Message}", warn: true);
            SetCatalogRefreshVisual(CatalogSyncButtonState.Error);
        }
        finally
        {
            HideCatalogUpdateOverlay();
            _catalogLoadBusy = false;
            if (RefreshCatalogButton != null)
                RefreshCatalogButton.IsEnabled = true;
            UpdateCatalogPagerUi();
        }
    }

    private void CatalogUpdateLater_Click(object sender, RoutedEventArgs e)
    {
        if (_catalogUpdateOverlayBusy)
            return;

        HideCatalogUpdateOverlay();
        App.CatalogBackgroundSync.NotifyPostponed();
    }

    internal void SetCatalogRefreshVisual(CatalogSyncButtonState state)
    {
        if (RefreshCatalogIcon == null)
            return;

        _refreshIconDefaultBrush ??= RefreshCatalogIcon.Foreground;
        _catalogRefreshSpinStoryboard?.Stop();
        _catalogRefreshSpinStoryboard = null;

        switch (state)
        {
            case CatalogSyncButtonState.Syncing:
                RefreshCatalogIcon.Foreground = _refreshIconDefaultBrush;
                var animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                };
                _catalogRefreshSpinStoryboard = new Storyboard();
                Storyboard.SetTarget(animation, RefreshCatalogIconRotate);
                Storyboard.SetTargetProperty(animation, new PropertyPath(RotateTransform.AngleProperty));
                _catalogRefreshSpinStoryboard.Children.Add(animation);
                _catalogRefreshSpinStoryboard.Begin();
                break;
            case CatalogSyncButtonState.UpdateAvailable:
                RefreshCatalogIcon.Foreground = UiWarn;
                if (RefreshCatalogIconRotate != null)
                    RefreshCatalogIconRotate.Angle = 0;
                break;
            case CatalogSyncButtonState.Error:
                RefreshCatalogIcon.Foreground = ThemeBrush("BrushUiStatusError", new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)));
                if (RefreshCatalogIconRotate != null)
                    RefreshCatalogIconRotate.Angle = 0;
                break;
            default:
                RefreshCatalogIcon.Foreground = _refreshIconDefaultBrush;
                if (RefreshCatalogIconRotate != null)
                    RefreshCatalogIconRotate.Angle = 0;
                break;
        }
    }

    // Вспомогательный метод — восстановление из кэша
    private bool TryRestoreFromCache()
    {
        if (!CatalogCacheService.LoadFromDatabase())
            return false;

        RestoreTilesFromCache();
        SortByFavorite();
        ResetCatalogViewport();
        UpdateCacheStatus();
        ShowToast($"Офлайн: каталог из локальной БД ({CatalogCacheService.Products.Count} шт.)", warn: false);
        return true;
    }

    private void ResetCatalogViewport()
    {
        _catalogVisibleLimitKg = CatalogPageSize;
        _catalogVisibleLimitPiece = CatalogPageSize;
        ResetAllProductsPage();
        ApplyCatalogViewport();
    }

    private void ResetActiveCatalogVisibleLimit()
    {
        switch (GetSelectedCatalogTab())
        {
            case CatalogTabKind.Piece:
                _catalogVisibleLimitPiece = CatalogPageSize;
                break;
            case CatalogTabKind.Weight:
                _catalogVisibleLimitKg = CatalogPageSize;
                break;
            default:
                ResetAllProductsPage();
                break;
        }
    }

    private void EnsureActiveCatalogPageVisible()
    {
        ApplyCatalogViewport();
    }

    private void ApplyCatalogViewport()
    {
        if (!string.IsNullOrEmpty(_catalogSearchFilter))
        {
            UpdateCatalogCount();
            UpdateCatalogPagerUi();
            WarmVisibleWeighedThumbs();
            return;
        }

        if (GetSelectedCatalogTab() == CatalogTabKind.All)
        {
            UpdateCatalogCount();
            UpdateCatalogPagerUi();
            WarmVisibleWeighedThumbs();
            if (_tilesAll.Count == 0 && !_allProductsLoading)
                _ = LoadAllProductsPageAsync(append: false);
            return;
        }

        _tilesKg.Reset(GetDisplayList(_allTilesKg, _catalogVisibleLimitKg));
        _tilesPiece.Reset(GetDisplayList(_allTilesPiece, _catalogVisibleLimitPiece));
        OnPropertyChanged(nameof(CatalogTableSource));
        UpdateCatalogCount();
        UpdateCatalogPagerUi();
        WarmVisibleWeighedThumbs();
    }

    private async Task LoadAllProductsPageAsync(bool append)
    {
        if (_allProductsLoading)
            return;

        _allProductsLoading = true;
        try
        {
            var offset = append ? _tilesAll.Count : 0;
            if (!append)
                _tilesAll.Clear();

            var result = await _productSearchService
                .LoadAllProductsPageAsync(
                    offset,
                    AllProductsPageSize,
                    _currentFilter,
                    _windowCts.Token)
                .ConfigureAwait(true);

            await RunOnUiAsync(() =>
            {
                foreach (var item in result.Items)
                    _tilesAll.Add(item);

                _allProductsHasMore = result.HasMore;
                UpdateCatalogCount();
                UpdateCatalogPagerUi();
                WarmVisibleWeighedThumbs();
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() => ShowToast($"Каталог: {ex.Message}", warn: true));
        }
        finally
        {
            _allProductsLoading = false;
        }
    }

    private List<CatalogProductTileVm> GetDisplayList(IReadOnlyList<CatalogProductTileVm> source, int visibleLimit)
    {
        IEnumerable<CatalogProductTileVm> query = source;
        if (_currentFilter != null)
            query = source.Where(FilterPredicate);

        return query.Take(visibleLimit).ToList();
    }

    private int GetFilteredSourceCount(IReadOnlyList<CatalogProductTileVm> source)
    {
        if (_currentFilter == null)
            return source.Count;

        return source.Count(FilterPredicate);
    }

    private void UpdateCatalogPagerUi()
    {
        if (CatalogMoreButton == null)
            return;

        if (!string.IsNullOrEmpty(_catalogSearchFilter))
        {
            CatalogMoreButton.Visibility = _searchHasMore
                ? Visibility.Visible
                : Visibility.Collapsed;
            return;
        }

        if (GetSelectedCatalogTab() == CatalogTabKind.All)
        {
            CatalogMoreButton.Visibility = _allProductsHasMore
                ? Visibility.Visible
                : Visibility.Collapsed;
            return;
        }

        var isPiece = GetSelectedCatalogTab() == CatalogTabKind.Piece;
        var visibleCount = isPiece ? _tilesPiece.Count : _tilesKg.Count;
        var totalCount = GetFilteredSourceCount(isPiece ? _allTilesPiece : _allTilesKg);
        CatalogMoreButton.Visibility = visibleCount < totalCount
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void CatalogMore_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_catalogSearchFilter))
        {
            _searchOffset += GetActiveCatalogPageSize();
            await RunCatalogSearchAsync(append: true).ConfigureAwait(true);
            return;
        }

        if (GetSelectedCatalogTab() == CatalogTabKind.All)
        {
            await LoadAllProductsPageAsync(append: true).ConfigureAwait(true);
            return;
        }

        if (GetSelectedCatalogTab() == CatalogTabKind.Piece)
            _catalogVisibleLimitPiece += CatalogPageSize;
        else
            _catalogVisibleLimitKg += CatalogPageSize;

        ApplyCatalogViewport();
    }

    private void CatalogSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _catalogSearchFilter = (CatalogSearchBox.Text ?? "").Trim();
        _pendingSearchQuery = _catalogSearchFilter;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(_catalogSearchFilter)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (string.IsNullOrEmpty(_catalogSearchFilter))
        {
            _searchDebounceTimer?.Stop();
            RunOnUi(() =>
            {
                _allSearchTiles.Clear();
                _searchTiles.Clear();
                SearchOverlayPanel.Visibility = Visibility.Collapsed;
                _searchOffset = 0;
                _searchHasMore = false;
            });
            ResetCatalogViewport();
            return;
        }

        if (_pendingSearchQuery.Length >= 2)
        {
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer?.Start();
        }
        else
        {
            _searchDebounceTimer?.Stop();
            RunOnUi(() =>
            {
                if (GetSelectedCatalogTab() == CatalogTabKind.All)
                    _tilesAll.Clear();
                else if (GetSelectedCatalogTab() == CatalogTabKind.Piece)
                    _tilesPiece.Clear();
                else
                    _tilesKg.Clear();
                _searchOffset = 0;
                _searchHasMore = false;
                SearchOverlayPanel.Visibility = Visibility.Collapsed;
                UpdateCatalogPagerUi();
            });
        }
    }

    private async Task RunCatalogSearchAsync(bool append)
    {
        var query = _catalogSearchFilter;
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return;

        if (!append)
            _searchOffset = 0;

        var tab = GetSelectedCatalogTab();
        var pageSize = GetActiveCatalogPageSize();

        try
        {
            ProductSearchResult result = tab switch
            {
                CatalogTabKind.All => await _productSearchService.SearchAllProductsAsync(
                    query,
                    _searchOffset,
                    pageSize,
                    _currentFilter,
                    _windowCts.Token).ConfigureAwait(false),
                CatalogTabKind.Piece => await _productSearchService.SearchProductsAsync(
                    query,
                    isWeightCategory: false,
                    currentOffset: _searchOffset,
                    pageSize: pageSize,
                    cancellationToken: _windowCts.Token).ConfigureAwait(false),
                _ => await _productSearchService.SearchProductsAsync(
                    query,
                    isWeightCategory: true,
                    currentOffset: _searchOffset,
                    pageSize: pageSize,
                    cancellationToken: _windowCts.Token).ConfigureAwait(false),
            };

            await RunOnUiAsync(() =>
            {
                var target = tab switch
                {
                    CatalogTabKind.All => (BulkObservableCollection<CatalogProductTileVm>)_tilesAll,
                    CatalogTabKind.Piece => _tilesPiece,
                    _ => _tilesKg,
                };

                if (!append)
                    target.Reset(result.Items);
                else
                {
                    foreach (var item in result.Items)
                        target.Add(item);
                }

                _searchHasMore = result.HasMore;
                SearchOverlayPanel.Visibility = Visibility.Collapsed;
                UpdateCatalogCount();
                UpdateCatalogPagerUi();
                WarmVisibleWeighedThumbs();
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() => ShowToast($"Поиск: {ex.Message}", warn: true));
        }
    }

    private async void CatalogGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CatalogGrid.SelectedItem is CatalogProductTileVm vm)
            await PickProductFromCatalogAsync(vm);
    }

    private void ToggleToolsPanel_Click(object sender, RoutedEventArgs e)
    {
        _moreCartActionsVisible = !_moreCartActionsVisible;
        if (MoreCartActionsPanel != null)
            MoreCartActionsPanel.Visibility = _moreCartActionsVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void DeleteReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (HasActiveCartLines)
        {
            var answer = PosMessageBox.Show(
                this,
                "Вы действительно хотите удалить чек?",
                "Удаление чека",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        if (IsViewingDeferredReceipt)
        {
            //// === Вариант 1 (если есть StagingCartService.DeleteDeferredCart) ===
            //StagingCartService.DeleteDeferredCart(_viewingDeferredEntryId!);

            // === Вариант 2 (если метода нет – удаляем вручную) ===
            var all = DeferredCartsStore.LoadAll().ToList();
            all.RemoveAll(e => e.Id == _viewingDeferredEntryId);
            DeferredCartsStore.SaveAll(all);   // или перезаписать хранилище

            // Очищаем корзину, сохраняя флаг Staging
            var emptyStaging = CartJsonHelper.CreateEmptyCart();
            emptyStaging["is_staging"] = true;
            using var doc = JsonDocument.Parse(emptyStaging.ToJsonString());
            _cart.SetCart(doc.RootElement);

            _viewingDeferredEntryId = null;
            RebuildReceiptTabStrip();

            if (!DeferredCartsStore.LoadAll().Any())
            {
                _cart.Clear();
                await StartNewSaleCoreAsync().ConfigureAwait(true);
            }
            return;
        }

        await StartNewSaleCoreAsync().ConfigureAwait(true);
    }

    private void ManualAddQtyMinus_Click(object sender, RoutedEventArgs e) =>
        AdjustManualAddQtyCounter(-1, null);

    private void ManualAddQtyPlus_Click(object sender, RoutedEventArgs e) =>
        AdjustManualAddQtyCounter(1, null);

    private void AdjustManualAddQtyCounter(int direction, bool? weighedHint)
    {
        if (ManualAddQtyBox == null)
            return;

        var isWeight = weighedHint == true;
        if (weighedHint == null && double.TryParse(ManualAddQtyBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var current))
            isWeight = Math.Abs(current % 1) > 1e-6 || ManualAddQtyBox.Text.Contains('.') || ManualAddQtyBox.Text.Contains(',');

        if (isWeight)
        {
            var step = (double)JsonNumericReader.WeightStepKg;
            var q = ParseManualAddQty(false) + direction * step;
            SetManualAddQtyText(Math.Max((double)JsonNumericReader.WeightStepKg, JsonNumericReader.RoundWeight(q)), weighed: true);
            return;
        }

        SetManualAddQtyText(Math.Clamp((int)Math.Round(ParseManualAddQty(false)) + direction, 1, 999));
    }

    private double ParseManualAddQty(bool weighed)
    {
        if (ManualAddQtyBox == null)
            return weighed ? 0.001 : 1;

        var text = (ManualAddQtyBox.Text ?? "").Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
            return weighed ? 0.001 : 1;

        return weighed ? Math.Round(qty, 3) : Math.Round(qty, 0);
    }

    private void SetManualAddQtyText(double qty, bool weighed = false)
    {
        if (ManualAddQtyBox == null)
            return;

        ManualAddQtyBox.Text = weighed || Math.Abs(qty % 1) > 1e-6
            ? qty.ToString("0.###", CultureInfo.InvariantCulture)
            : Math.Round(qty, 0).ToString(CultureInfo.InvariantCulture);
    }

    private void ResetManualAddQtyIfNeeded(bool weighed)
    {
        if (!UserPreferences.Instance.ResetManualAddQtyAfterAdd)
            return;

        SetManualAddQtyText(1, weighed);
    }

    private bool TryGetManualAddQuantity(CatalogProductTileVm product, out double qty, out string error)
    {
        error = string.Empty;
        qty = ParseManualAddQty(product.MustWeigh);

        if (!product.MustWeigh && Math.Abs(qty % 1) > 1e-6)
        {
            error = "Для штучного товара можно указать только целое количество.";
            return false;
        }

        if (qty <= 0)
        {
            error = "Укажите количество больше нуля.";
            return false;
        }

        qty = product.MustWeigh ? Math.Round(qty, 3) : Math.Round(qty, 0);
        return true;
    }

    private static string FormatAddLogQuantity(double qty, bool weighed) =>
        weighed
            ? qty.ToString("0.###", CultureInfo.InvariantCulture)
            : Math.Round(qty, 0).ToString(CultureInfo.InvariantCulture);

    private void RebuildReceiptTabStrip()
    {
        if (ReceiptTabsHost == null)
            return;

        ReceiptTabsHost.Children.Clear();

        var deferred = DeferredCartsStore.LoadAll().OrderByDescending(x => x.SavedAt).ToList();
        var onPrimary = _viewingDeferredEntryId == null;
        var (primaryLines, primaryTotal) = onPrimary
            ? (CartLines.Count, ParseCartTotalFromRoot(_cart.HasCart ? _cart.Root : default))
            : GetCartSummaryFromJson(_primaryReceiptSnapshot.CartJson);
        ReceiptTabsHost.Children.Add(CreateReceiptTabButton(
            FormatReceiptTabLabel(1, primaryLines, primaryTotal),
            isActive: onPrimary,
            entryId: null,
            isPrimary: true));

        for (var i = 0; i < deferred.Count; i++)
        {
            var entry = deferred[i];
            var (lines, total) = string.Equals(entry.Id, _viewingDeferredEntryId, StringComparison.Ordinal)
                ? (CartLines.Count, ParseCartTotalFromRoot(_cart.HasCart ? _cart.Root : default))
                : GetCartSummaryFromJson(entry.CartJson);
            var label = FormatReceiptTabLabel(i + 2, lines, total);
            var isActive = string.Equals(entry.Id, _viewingDeferredEntryId, StringComparison.Ordinal);
            ReceiptTabsHost.Children.Add(CreateReceiptTabButton(label, isActive: isActive, entryId: entry.Id));
        }

        var newReceiptButton = new Button
        {
            Content = "+ Новый чек",
            Style = (Style)FindResource("ReceiptTabButton"),
            Padding = new Thickness(14, 7, 14, 7),
        };
        newReceiptButton.Click += StartSale_Click;
        ReceiptTabsHost.Children.Add(newReceiptButton);
    }

    private static string FormatReceiptTabLabel(int receiptNumber, int itemCount, double total)
    {
        var title = receiptNumber == 1 ? "Основной чек" : $"Чек {receiptNumber}";
        return $"{title} • {itemCount} тов. • {CartTotalsCalculator.FormatMoney(total)} сом";
    }

    private static (int Lines, double Total) GetCartSummaryFromJson(string cartJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(cartJson) ? "{}" : cartJson);
            var root = doc.RootElement;
            var lines = CartDisplayHelper.EnumerateItems(root).Count();
            var total = CartTotalsCalculator.Calculate(root).TotalDue;
            return (lines, total);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static double ParseCartTotalFromRoot(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object ? CartTotalsCalculator.Calculate(root).TotalDue : 0;

    private Button CreateReceiptTabButton(string label, bool isActive, string? entryId, bool isPrimary = false)
    {
        var button = new Button
        {
            Content = label,
            Tag = entryId,
            Style = (Style)FindResource(isActive ? "ReceiptTabButtonActive" : "ReceiptTabButton"),
        };

        if (isActive)
            return button;

        if (isPrimary)
            button.Click += PrimaryReceiptTab_Click;
        else
            button.Click += ReceiptTab_Click;

        return button;
    }

    private static bool ReceiptTabKeyEquals(string? left, string? right) =>
        string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);

    private void RememberReceiptReturnTarget() => _receiptReturnTargetId = _viewingDeferredEntryId;

    private bool CanReturnToPreviousReceipt() =>
        !ReceiptTabKeyEquals(_viewingDeferredEntryId, _receiptReturnTargetId)
        && (_viewingDeferredEntryId != null || _receiptReturnTargetId != null);

    private void PrimaryReceiptTab_Click(object sender, RoutedEventArgs e) =>
        SwitchToPrimaryReceipt();

    private void ReceiptTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string entryId } || string.IsNullOrEmpty(entryId))
            return;

        SwitchToDeferredReceipt(entryId);
    }

    /// <summary>Сохраняет текущий _cart в снимок активной вкладки (память + файл для отложенных).</summary>
    private void PersistCurrentReceiptSnapshot()
    {
        if (!_cart.HasCart)
            return;

        var snapshot = OpenReceiptSnapshot.Capture(_cart);
        if (_viewingDeferredEntryId == null)
        {
            _primaryReceiptSnapshot = snapshot;
            return;
        }

        var entry = DeferredCartsStore.TryGetById(_viewingDeferredEntryId);
        if (entry == null)
            return;

        entry.CartJson = OpenReceiptSnapshot.CloneCartJson(snapshot.CartJson);
        entry.ServerCartId = _cart.IsStaging ? null : snapshot.ServerCartId;
        DeferredCartsStore.UpdateEntry(entry);
    }

    private void SwitchToPrimaryReceipt()
    {
        if (_receiptSwitchBusy || _viewingDeferredEntryId == null)
            return;

        var sw = Stopwatch.StartNew();
        _receiptSwitchBusy = true;
        try
        {
            RememberReceiptReturnTarget();
            PosLogger.Log("RECEIPT switch: deferred → основной чек (память)", "RECEIPT");
            PersistCurrentReceiptSnapshot();
            _primaryReceiptSnapshot.ApplyTo(_cart);
            _viewingDeferredEntryId = null;
            SyncActiveReceiptContext();
            RebindCartUi();
            LogReceiptOpenDiagnostics();
            PosLogger.Log($"RECEIPT switch done in {sw.ElapsedMilliseconds} ms", "RECEIPT");
        }
        finally
        {
            _receiptSwitchBusy = false;
        }
    }

    /// <summary>Автоматически закрывает отложенный чек, если в нём не осталось позиций.</summary>
    private void TryCloseEmptyDeferredReceipt()
    {
        if (!IsViewingDeferredReceipt || CartLines.Count > 0)
            return;

        var closedId = _viewingDeferredEntryId;
        DeferredCartsStore.RemoveIds(new[] { closedId! });
        _viewingDeferredEntryId = null;
        _primaryReceiptSnapshot.ApplyTo(_cart);
        PosLogger.Log($"RECEIPT: empty deferred auto-closed id={closedId}", "RECEIPT");
        RebindCartUi();
    }

    private void SwitchToDeferredReceipt(string entryId)
    {
        if (_receiptSwitchBusy ||
            string.Equals(_viewingDeferredEntryId, entryId, StringComparison.Ordinal))
            return;

        var entry = DeferredCartsStore.TryGetById(entryId);
        if (entry == null)
        {
            RebuildReceiptTabStrip();
            return;
        }

        var sw = Stopwatch.StartNew();
        _receiptSwitchBusy = true;
        try
        {
            RememberReceiptReturnTarget();
            PosLogger.Log($"RECEIPT switch: → отложенный id={entryId} (память)", "RECEIPT");
            PersistCurrentReceiptSnapshot();

            OpenReceiptSnapshot.ApplyDeferredStaging(_cart, entry.CartJson);
            _viewingDeferredEntryId = entryId;

            // --- Исправление повреждённых/отсутствующих product_id ---
            var root = CartJsonHelper.ParseCartRoot(_cart);
            var items = root["items"] as JsonArray ?? new JsonArray();
            ReceiptSnapshotCartEditor.RepairMissingProductIds(items);
            root["items"] = items;
            CartJsonHelper.TryApplyObjectToCart(_cart, root);
            // ---------------------------------------------------------

            RebindCartUi();
            ValidateDeferredStockOnRestore();
            SyncActiveReceiptContext();
            LogReceiptOpenDiagnostics();
            PosLogger.Log($"RECEIPT switch done in {sw.ElapsedMilliseconds} ms, lines={CartLines.Count}", "RECEIPT");
        }
        finally
        {
            _receiptSwitchBusy = false;
        }
    }

    private void CapturePrimaryReceiptSnapshot()
    {
        if (_viewingDeferredEntryId != null)
            return;

        ResetPrimaryReceiptSnapshotForNewActiveReceipt();
    }

    private async void SearchDebounce_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer?.Stop();
        if (_pendingSearchQuery.Length < 2)
            return;

        await RunCatalogSearchAsync(append: false).ConfigureAwait(true);
    }

    private async void RefreshCatalog_Click(object sender, RoutedEventArgs e) =>
        await LoadCatalogAsync(_windowCts.Token, manual: true).ConfigureAwait(true);

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

        UpdateCatalogTabVisibility();
        ResetActiveCatalogVisibleLimit();
        _searchOffset = 0;
        if (!string.IsNullOrEmpty(_catalogSearchFilter) && _catalogSearchFilter.Length >= 2)
            _ = RunCatalogSearchAsync(append: false);
        else
            ApplyCatalogViewport();
        ResetCatalogListScroll(GetActiveCatalogListView());
    }

    private void UpdateCatalogTabVisibility()
    {
        var tab = GetSelectedCatalogTab();
        if (CatalogItemsAll != null)
            CatalogItemsAll.Visibility = tab == CatalogTabKind.All ? Visibility.Visible : Visibility.Collapsed;
        if (CatalogItemsKg != null)
            CatalogItemsKg.Visibility = tab == CatalogTabKind.Weight ? Visibility.Visible : Visibility.Collapsed;
        if (CatalogItemsPiece != null)
            CatalogItemsPiece.Visibility = tab == CatalogTabKind.Piece ? Visibility.Visible : Visibility.Collapsed;
    }

    private ListView? GetActiveCatalogListView() =>
        GetSelectedCatalogTab() switch
        {
            CatalogTabKind.Piece => CatalogItemsPiece,
            CatalogTabKind.Weight => CatalogItemsKg,
            _ => CatalogItemsAll,
        };

    private static void ResetCatalogListScroll(ListView? listView)
    {
        if (listView == null)
            return;

        listView.Dispatcher.BeginInvoke(() =>
        {
            if (FindVisualChild<ScrollViewer>(listView) is ScrollViewer sv)
                sv.ScrollToVerticalOffset(0);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ApplySearchViewport()
    {
        _searchTiles.Reset(_allSearchTiles);
        if (SearchOverlayTitle != null)
            SearchOverlayTitle.Text = _allSearchTiles.Count == 0
                ? "Поиск"
                : $"Поиск «{_pendingSearchQuery}» — {_allSearchTiles.Count}";
        WarmVisibleWeighedThumbs();
    }

    private void WarmVisibleWeighedThumbs()
    {
        try
        {
            if (_windowCts.IsCancellationRequested)
                return;
            _ = WarmVisibleWeighedThumbsAsync(_windowCts.Token);
        }
        catch (ObjectDisposedException)
        {
            /* окно закрывается */
        }
    }

    private async Task WarmVisibleWeighedThumbsAsync(CancellationToken cancellationToken)
    {
        var apiBase = App.Settings.ApiBaseUrl;
        var visible = _tilesAll
            .Concat(_tilesKg)
            .Concat(_searchTiles)
            .Where(vm => vm.MustWeigh && string.IsNullOrEmpty(vm.ProductImagePath) && !string.IsNullOrWhiteSpace(vm.ImageUrl))
            .ToList();

        foreach (var vm in visible)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _catalogThumbService
                    .SetThumbAsync(Dispatcher, App.AuthApi, apiBase, vm.ImageUrl!, vm, cancellationToken)
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

    private void SearchMore_Click(object sender, RoutedEventArgs e) =>
        ApplySearchViewport();

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

    private Task PickProductFromCatalogAsync(CatalogProductTileVm vm) =>
        RunOnUiAsync(() => PickProductFromCatalogCoreAsync(vm));

    private Task PickProductFromCatalogCoreAsync(CatalogProductTileVm vm) =>
        AddProductToActiveCartAsync(vm, useWeighDialogForWeight: true);

    private Task AddProductByLookupAsync(string code) =>
        ProcessBarcodeScanAsync(code);

    private async Task AddProductToActiveCartAsync(
        CatalogProductTileVm vm,
        bool useWeighDialogForWeight,
        double? presetQuantity = null)
    {
        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return;
        if (!await EnsureSaleSessionReadyAsync().ConfigureAwait(true))
            return;

        string? qty = null;
        double qtyToAdd;

        if (presetQuantity is > 0)
        {
            qtyToAdd = vm.MustWeigh
                ? Math.Round(presetQuantity.Value, 3)
                : Math.Round(presetQuantity.Value, 0);
            qty = FormatQuantityForApi(qtyToAdd, vm.MustWeigh);
        }
        else if (vm.MustWeigh && useWeighDialogForWeight)
        {
            var dlg = new WeighedProductDialog(
                vm.Title,
                vm.PriceLine,
                GetScaleForWeighDialog()) { Owner = this };
            if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.QuantityNormalized))
                return;
            qty = dlg.QuantityNormalized;
            if (!double.TryParse(qty, NumberStyles.Any, CultureInfo.InvariantCulture, out qtyToAdd) || qtyToAdd <= 0)
                return;
        }
        else if (!TryGetManualAddQuantity(vm, out qtyToAdd, out var qtyError))
        {
            PosMessageBox.Show(
                string.IsNullOrWhiteSpace(qtyError) ? "Укажите корректное количество." : qtyError,
                "Количество",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        else
        {
            qty = FormatQuantityForApi(qtyToAdd, vm.MustWeigh);
        }

        PosLogger.Log(
            $"Product={vm.Barcode ?? vm.Id} Quantity={FormatAddLogQuantity(qtyToAdd, vm.MustWeigh)} Type={(vm.MustWeigh ? "Weight" : "Piece")}",
            "ADD");

        if (!StockAvailabilityService.CanAddQuantity(vm.Id, qtyToAdd, _cart, _viewingDeferredEntryId))
        {
            ShowNoStockBlocked(vm.Title, vm.Id);
            return;
        }

        SetScanBusy(true);
        try
        {
            if (OfflineModeHelper.UseLocalOperations || _cart.IsLocalOffline)
            {
                LocalCartService.AddProduct(_cart, vm, qty);
                if (vm.MustWeigh)
                    CartDisplayHelper.HintProductWeighedForDisplay(vm.Id);
                RebindCartUi();
                CartMessageText.Text = "Товар добавлен (офлайн).";
                CartMessageText.Foreground = UiOk;
                BarcodeBox.Clear();
                CatalogSearchBox.Text = "";
                SearchOverlayPanel.Visibility = Visibility.Collapsed;
                ShowToast(vm.MustWeigh ? $"Добавлено {qty} кг" : $"Добавлено {FormatAddLogQuantity(qtyToAdd, false)} шт");
                ResetManualAddQtyIfNeeded(vm.MustWeigh);
                return;
            }

            if (UseSnapshotEditing)
            {
                ReceiptSnapshotCartEditor.AddProduct(_cart, vm, qtyToAdd);
                if (vm.MustWeigh)
                    CartDisplayHelper.HintProductWeighedForDisplay(vm.Id);
                CartMessageText.Text = "Товар добавлен.";
                CartMessageText.Foreground = UiOk;
                BarcodeBox.Clear();
                CatalogSearchBox.Text = "";
                SearchOverlayPanel.Visibility = Visibility.Collapsed;
                ShowToast(vm.MustWeigh ? $"Добавлено {qty} кг" : $"Добавлено {FormatAddLogQuantity(qtyToAdd, false)} шт");
                AfterDeferredCartMutation();
                ResetManualAddQtyIfNeeded(vm.MustWeigh);
                return;
            }

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var resp = await App.SalesApi.PosAddItemAsync(_cart.CartId!, vm.Id, qty).ConfigureAwait(true);
                    if (!CartResponseHelper.TryUpdateCartSession(resp, _cart))
                        await ReloadCartFromServerAsync().ConfigureAwait(true);
                    if (vm.MustWeigh)
                        CartDisplayHelper.HintProductWeighedForDisplay(vm.Id);
                    if (IsViewingDeferredReceipt)
                        PersistCurrentReceiptSnapshot();
                    RebindCartUi();
                    CartMessageText.Text = "Товар добавлен.";
                    CartMessageText.Foreground = UiOk;
                    BarcodeBox.Clear();
                    CatalogSearchBox.Text = "";
                    SearchOverlayPanel.Visibility = Visibility.Collapsed;
                    ShowToast(vm.MustWeigh ? $"Добавлено {qty} кг" : $"Добавлено {FormatAddLogQuantity(qtyToAdd, false)} шт");
                    ResetManualAddQtyIfNeeded(vm.MustWeigh);
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
        if (!_cart.CanRefresh)
        {
            ShowToast("Нет активной корзины.", warn: true);
            return;
        }

        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return;

        // ===== ВОТ СТАРЫЙ ВЫЗОВ ОКНА =====
        var dlg = new OrderDiscountDialog(
            GetCurrentOrderDiscountPercent(),
            GetCurrentOrderDiscountSum());

        if (ShowModalDialog(dlg) != true)
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

    private async Task<bool> TryApplyCheckoutDiscountAsync(Dictionary<string, string> body)
    {
        var isClear = body.Count == 1
                      && body.Values.All(OrderDiscountHelper.IsEmptyOrZeroLike);
        var patchBody = OrderDiscountHelper.SanitizePatchBody(body, isClear);
        if (patchBody.Count == 0)
            return true;

        patchBody.TryGetValue("order_discount_percent", out var pct);
        patchBody.TryGetValue("order_discount_total", out var sum);

        if (UseSnapshotEditing)
        {
            ReceiptSnapshotCartEditor.PatchOrderDiscount(_cart, pct, sum);
            AfterDeferredCartMutation();
            return true;
        }

        if (OfflineModeHelper.UseLocalOperations || _cart.IsLocalOffline)
        {
            LocalCartService.PatchOrderDiscount(_cart, pct, sum);
            RebindCartUi();
            return true;
        }

        if (App.Api == null)
            return false;

        return await ApplyOrderDiscountBodyAsync(patchBody, "", showToast: false, paymentContext: true)
            .ConfigureAwait(true);
    }

    private async Task<bool> ApplyOrderDiscountBodyAsync(
        Dictionary<string, string> body,
        string successMessage,
        bool showToast = true,
        bool paymentContext = false)
    {
        var isClear = body.Count == 1
                      && body.Values.All(OrderDiscountHelper.IsEmptyOrZeroLike);
        var patchBody = OrderDiscountHelper.SanitizePatchBody(body, isClear);
        if (patchBody.Count == 0)
        {
            RebindCartUi();
            return true;
        }

        SetScanBusy(true);
        try
        {
            var resp = await App.SalesApi.PosCartPatchAsync(_cart.CartId!, patchBody).ConfigureAwait(true);
            if (!CartResponseHelper.TryUpdateCartSession(resp, _cart))
                await ReloadCartFromServerAsync().ConfigureAwait(true);
            RebindCartUi();
            if (showToast && !string.IsNullOrEmpty(successMessage))
                ShowToast(successMessage);
            return true;
        }
        catch (ApiException ex)
        {
            if (paymentContext)
                PaymentErrorMessages.Log("Скидка перед оплатой", ex);
            else
                PosLogger.Log($"Скидка на чек: {ex.Message}", "ERROR");

            if (showToast)
                ShowToast(PaymentErrorMessages.LooksLikeDiscountError(ex.Message)
                    ? PaymentErrorMessages.DiscountFailure
                    : "Не удалось применить скидку.", warn: true);
            return false;
        }
        catch (HttpRequestException ex)
        {
            var msg = string.IsNullOrWhiteSpace(ex.Message) ? "Нет сети." : ex.Message;
            if (paymentContext)
                PaymentErrorMessages.Log("Скидка перед оплатой (сеть)", ex);
            else
                PosLogger.Log($"Скидка на чек (сеть): {msg}", "ERROR");

            if (showToast)
                ShowToast(paymentContext ? PaymentErrorMessages.GenericFailure : msg, warn: true);
            return false;
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private async Task ClearOrderDiscountAsync()
    {
        if (!_cart.CanRefresh)
            return;

        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return;

        SetScanBusy(true);
        try
        {
            var clearBody = OrderDiscountHelper.BuildClearPatchBody(
                GetCurrentOrderDiscountPercent(),
                GetCurrentOrderDiscountSum());
            if (clearBody.Count == 0)
            {
                RebindCartUi();
                ShowToast("Скидка на чек сброшена.");
                return;
            }

            var resp = await App.SalesApi
                .PosCartPatchAsync(_cart.CartId!, clearBody)
                .ConfigureAwait(true);
            if (!CartResponseHelper.TryUpdateCartSession(resp, _cart))
                await ReloadCartFromServerAsync().ConfigureAwait(true);

            RebindCartUi();
            ShowToast("Скидка на чек сброшена.");
        }
        catch (ApiException ex)
        {
            PosLogger.Log($"Сброс скидки: {ex.Message}", "ERROR");
            ShowToast(PaymentErrorMessages.LooksLikeDiscountError(ex.Message)
                ? PaymentErrorMessages.DiscountFailure
                : "Не удалось сбросить скидку.", warn: true);
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
        SearchOverlayPanel.Visibility = Visibility.Collapsed;
    }

    private void MenuShift_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        var dlg = new CashOperationsDialog
        {
            Owner = this,
            OpenShiftAction = async (openingCash) => await OpenShiftWithCashAsync(openingCash),
            CloseShiftAction = async (closingCash) => await CloseShiftWithCashAsync(closingCash)
        };
        dlg.ShowDialog();
    }

    public async Task OpenShiftWithCashAsync(decimal openingCash)
    {
        var result = await _cashShiftService
            .OpenShiftAsync(openingCash, _windowCts.Token)
            .ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            PosMessageBox.Show(result.ErrorMessage ?? "Ошибка открытия смены.", "Смена", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _shiftCashBalance = result.Balance;
        UpdateShiftBanner();

        if (result.IsOffline)
            ShowToast(result.InfoMessage ?? $"Смена открыта офлайн. Остаток: {openingCash:0.00} сом", warn: true);
        else if (!string.IsNullOrWhiteSpace(result.InfoMessage))
            ShowToast(result.InfoMessage, warn: false);
    }

    public async Task CloseShiftWithCashAsync(decimal? closingCash)
    {
        var result = await _cashShiftService
            .CloseShiftAsync(closingCash, _windowCts.Token)
            .ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            PosMessageBox.Show(result.ErrorMessage ?? "Ошибка закрытия смены.", "Смена", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _shiftCashBalance = result.Balance;
        UpdateShiftBanner();
    }


    private void MenuWarehouse_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        var warehouseWindow = App.GetRequiredService<WarehouseWindow>();
        warehouseWindow.Owner = this;
        warehouseWindow.Show();
    }

    private void MenuShiftsHistory_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        var shiftsWindow = new ShiftsHistoryWindow { Owner = this };
        shiftsWindow.Show();
    }

    private void MenuServices_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        var servicesWindow = new ServicesWindow { Owner = this };
        servicesWindow.ShowDialog();
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
        if (HardwareModeHelper.UseDemoHardware(App.Settings))
        {
            _ = TryPrintReceiptAsync(
                _cart.Root.GetRawText(),
                receiptText: "=== ТЕСТ ПЕЧАТИ (ДЕМО) ===\nNur Market Kassa\n");
            ShowToast("Тестовый чек отправлен в виртуальный принтер (лог).");
            return;
        }

        var cfg = UserPreferences.Instance.ToReceiptPrinterSettings();
        if (string.IsNullOrWhiteSpace(cfg.DevicePath))
        {
            PosMessageBox.Show(
                "Укажите LPT-порт в «Настройки кассы».",
                "Принтер",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!cfg.Enabled)
        {
            PosMessageBox.Show(
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
            PosMessageBox.Show(
                "Принтер: ошибка.\n\n" + ex.Message + "\n\nПроверьте LPT в настройках и кабель.",
                "Печать",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ScaleTest_Click(object sender, RoutedEventArgs e)
    {
        if (HardwareModeHelper.UseDemoHardware(App.Settings))
        {
            var demoWeight = _weightScale.LastWeight;
            PosMessageBox.Show(
                $"Демо-режим весов.\nСтатус: {_weightScale.Status}\nТекущий вес: {(demoWeight is > 0 ? $"{demoWeight:0.###} кг" : "—")}",
                "Тест весов",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var sp = UserPreferences.Instance;
        if (!sp.ScaleEnabled)
        {
            PosMessageBox.Show(
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
            PosMessageBox.Show(ex.Message, "Весы", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var st = _weightScale.Status;
        var w = _weightScale.LastWeight;
        PosMessageBox.Show(
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
        PosMessageBox.Show(
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
            PosMessageBox.Show(ex.Message, "Оффлайн оплата", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OfflineCheckout_ClickCoreAsync()
    {
        if (App.Api is null)
        {
            PosMessageBox.Show("Подключение не готово.", "Оффлайн оплата", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!HasCheckoutableCart())
        {
            PosMessageBox.Show("Добавьте товары в корзину.", "Оффлайн оплата", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (TryBlockCheckoutForStockIssues())
            return;

        try
        {
            if (IsViewingDeferredReceipt)
                await EnsureDeferredCartReadyForCheckoutAsync().ConfigureAwait(true);
            else if (!_cart.IsLocalOffline && !OfflineModeHelper.CanOperateWithoutServer)
                await EnsureActiveCartReadyForCheckoutAsync().ConfigureAwait(true);
        }
        catch (ApiException ex)
        {
            PosMessageBox.Show(ex.Message, "Оффлайн оплата", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var totals = CartTotalsCalculator.Calculate(_cart.Root);
        var dlg = new CheckoutDialog(
            totals,
            GetCurrentOrderDiscountPercent(),
            GetCurrentOrderDiscountSum())
        { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        if (dlg.PendingOrderDiscountBody != null)
        {
            if (!await TryApplyCheckoutDiscountAsync(dlg.PendingOrderDiscountBody).ConfigureAwait(true))
            {
                PosMessageBox.Show(
                    PaymentErrorMessages.DiscountFailure,
                    "Оплата",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        var paymentMethod = dlg.PaymentMethodKey;
        var cashReceived = dlg.CashReceivedForApi;
        var printReceipt = dlg.IsPrintReceiptEnabled;
        var saved = SaveCurrentSaleOffline(paymentMethod, cashReceived);
        var offlineTotal = CartDisplayHelper.TotalDue(JsonDocument.Parse(saved.CartJson).RootElement);

        await RunPostPaymentUiAsync(
            offlineTotal,
            paymentMethod,
            cashReceived,
            saved.CartJson,
            printReceipt,
            offlineNote: "ОФФЛАЙН (ожидает выгрузку)").ConfigureAwait(true);

        PosMessageBox.Show(
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
            CartJson = _cart.Root.GetRawText(),
            CartId = _cart.CartId,
            ShiftId = App.ActiveShiftId,
            BranchId = App.AuthApi.ActiveBranchId,
            CashboxId = App.PosCashboxId,
        };
        OfflinePendingSalesStore.Append(entry);
        ApplyOfflineStockDecrement(entry.CartJson);
        UpdateOfflineModeUi();
        return entry;
    }

    private static void ApplyOfflineStockDecrement(string cartJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(cartJson) ? "{}" : cartJson);
            foreach (var item in CartDisplayHelper.EnumerateItems(doc.RootElement))
            {
                var productId = CartDisplayHelper.TryProductId(item);
                if (string.IsNullOrEmpty(productId))
                    continue;

                var soldQty = CartDisplayHelper.LineQuantity(item);
                if (soldQty <= 0)
                    continue;

                var tile = CatalogCacheService.Products.FirstOrDefault(p =>
                    string.Equals(p.Id, productId, StringComparison.OrdinalIgnoreCase));
                if (tile == null)
                    continue;

                tile.Quantity = Math.Max(0, tile.Quantity - soldQty);
                LocalProductRepository.Instance.UpdateStock(productId, tile.Quantity, tile.MustWeigh);
            }
        }
        catch
        {
            /* остатки подтянутся при синхронизации каталога */
        }
    }

    private async void DeferCart_Click(object sender, RoutedEventArgs e)
    {
        await DeferCurrentCartAsync().ConfigureAwait(true);
    }

    private Task PrepareFreshSaleAfterDeferAsync(string? previousCartId, DeferredCartEntry deferredEntry)
    {
        SetScanBusy(true);
        try
        {
            _cart.Clear();
            StagingCartService.StartEmpty(_cart);
            ResetPrimaryReceiptSnapshotForNewActiveReceipt();
            RebindCartUi();
            LogDeferredCartSeparation(
                "DEFER new active receipt",
                deferredEntry.Id,
                deferredEntry.ServerCartId,
                deferredEntry.CartJson,
                previousCartId);
            CartMessageText.Text = "Новый чек открыт. Можно добавлять товары.";
            CartMessageText.Foreground = UiOk;
        }
        finally
        {
            SetScanBusy(false);
        }

        return Task.CompletedTask;
    }

    private void OpenDeferredCarts_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new DeferredCartsDialog(new DeferredCartsDialogActions
        {
            MergeIntoCurrentAsync = MergeDeferredIntoCurrentAsync,
            OpenAsSeparateAsync = OpenDeferredAsSeparateAsync,
        });
        ShowModalDialog(dlg);
        UpdateDeferredCartUi();
    }

    private int _modalDimmerRefCount;

    public void PushModalDimmer()
    {
        _modalDimmerRefCount++;
        ModalDimmer.Visibility = Visibility.Visible;
    }

    public void PopModalDimmer()
    {
        _modalDimmerRefCount = Math.Max(0, _modalDimmerRefCount - 1);
        if (_modalDimmerRefCount == 0)
            ModalDimmer.Visibility = Visibility.Collapsed;
    }

    /// <summary>Модальный диалог с затемнением главного окна.</summary>
    public bool? ShowModalDialog(Window dialog)
    {
        dialog.Owner = this;
        PushModalDimmer();
        try
        {
            return dialog.ShowDialog();
        }
        finally
        {
            PopModalDimmer();
        }
    }

    private async Task<bool> MergeDeferredIntoCurrentAsync(IReadOnlyList<DeferredCartEntry> entries)
    {
        if (entries.Count == 0)
            return false;

        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return false;

        SetScanBusy(true);
        try
        {
            foreach (var entry in entries)
            {
                if (!await AddDeferredEntryItemsToActiveCartAsync(entry, applyOrderDiscount: false).ConfigureAwait(true))
                    return false;

                DeferredCartsStore.RemoveIds(new[] { entry.Id });
            }

            if (IsViewingDeferredReceipt)
                AfterDeferredCartMutation();
            else
            {
                RebindCartUi();
                CapturePrimaryReceiptSnapshot();
            }

            UpdateDeferredCartUi();
            ShowToast($"Позиции из {entries.Count} отложенных чеков добавлены в текущий чек.");
            return true;
        }
        catch (ApiException ex)
        {
            PosMessageBox.Show(ex.Message, "Отложенные", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        catch (Exception ex)
        {
            PosMessageBox.Show(
                "Не удалось добавить отложенные позиции: " + ex.Message,
                "Отложенные",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private async Task<bool> OpenDeferredAsSeparateAsync(DeferredCartEntry entry)
    {
        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return false;

        SetScanBusy(true);
        try
        {
            if (HasActiveCartLines && !IsViewingDeferredReceipt)
            {
                if (!await DeferCurrentCartAsync(startNewSale: false, showToast: false).ConfigureAwait(true))
                    return false;
            }
            else
            {
                PersistCurrentReceiptSnapshot();
            }

            _viewingDeferredEntryId = null;
            ForceClearCartDisplay();

            if (OfflineModeHelper.UseLocalOperations)
            {
                LocalCartService.StartNewLocalCart(_cart);
            }
            else
            {
                await TryStartNewSaleAsync().ConfigureAwait(true);
            }

            if (!_cart.HasCart)
            {
                PosMessageBox.Show(
                    "Не удалось открыть новый чек.",
                    "Отложенные",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            if (!await AddDeferredEntryItemsToActiveCartAsync(entry, applyOrderDiscount: true).ConfigureAwait(true))
                return false;

            DeferredCartsStore.RemoveIds(new[] { entry.Id });
            CapturePrimaryReceiptSnapshot();
            RebindCartUi();
            UpdateDeferredCartUi();
            ShowToast("Отложенный чек открыт как новый.");
            return true;
        }
        catch (ApiException ex)
        {
            PosMessageBox.Show(ex.Message, "Отложенные", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        catch (Exception ex)
        {
            PosMessageBox.Show(
                "Не удалось открыть отложенный чек: " + ex.Message,
                "Отложенные",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private async Task<bool> AddDeferredEntryItemsToActiveCartAsync(
        DeferredCartEntry entry,
        bool applyOrderDiscount = false)
    {
        using var doc = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(entry.CartJson) ? "{}" : entry.CartJson);
        var root = doc.RootElement;
        var lines = CartDisplayHelper.EnumerateItems(root).ToList();
        if (lines.Count == 0)
            return true;

        if (!_cart.HasCart)
        {
            if (OfflineModeHelper.UseLocalOperations)
                LocalCartService.StartNewLocalCart(_cart);
            else
                await TryStartNewSaleAsync().ConfigureAwait(true);
        }

        foreach (var line in lines)
        {
            var productId = CartDisplayHelper.TryProductId(line);
            if (string.IsNullOrEmpty(productId))
                continue;

            var qty = CartDisplayHelper.LineQuantity(line);
            if (qty <= 0)
                continue;

            var product = ResolveCatalogProductForCartLine(line, productId);
            if (product == null)
                continue;

            if (OfflineModeHelper.UseLocalOperations || _cart.IsLocalOffline)
            {
                _cart.AddItem(product, qty);
                continue;
            }

            if (UseSnapshotEditing || _cart.IsStaging)
            {
                _cart.AddItem(product, qty);
                continue;
            }

            if (!_cart.CanRefresh || string.IsNullOrEmpty(_cart.CartId))
                throw new ApiException("Нет активной серверной корзины для добавления позиций.", 409);

            var qtyStr = FormatQuantityForApi(qty, product.MustWeigh);
            var unitPrice = CartDisplayHelper.FormatMoney(CartDisplayHelper.UnitPrice(line));
            var discount = CartDisplayHelper.OptionalDiscountTotalParam(line);
            var resp = await App.SalesApi
                .PosAddItemAsync(_cart.CartId!, productId, qtyStr, unitPrice, discount, CancellationToken.None)
                .ConfigureAwait(true);
            if (!CartResponseHelper.TryUpdateCartSession(resp, _cart))
                await ReloadCartFromServerAsync().ConfigureAwait(true);
        }

        if (applyOrderDiscount)
            await TryApplyDeferredOrderDiscountFromSnapshotAsync(root).ConfigureAwait(true);
        return true;
    }

    private CatalogProductTileVm? ResolveCatalogProductForCartLine(JsonElement line, string productId)
    {
        var fromCache = CatalogCacheService.Products.FirstOrDefault(p =>
            string.Equals(p.Id, productId, StringComparison.OrdinalIgnoreCase));
        if (fromCache != null)
            return fromCache;

        var title = CartDisplayHelper.ItemName(line);
        if (string.IsNullOrWhiteSpace(title))
            title = productId;

        var price = CartDisplayHelper.FormatMoney(CartDisplayHelper.UnitPrice(line));
        var mustWeigh = CartDisplayHelper.LineMustWeigh(line);
        return new CatalogProductTileVm(productId, title, price + " сом", mustWeigh);
    }

    private async Task TryApplyDeferredOrderDiscountFromSnapshotAsync(JsonElement cartRoot)
    {
        if (cartRoot.ValueKind != JsonValueKind.Object)
            return;

        var pct = cartRoot.TryGetProperty("order_discount_percent", out var p)
            ? FormatDiscountScalar(p)
            : "";
        var sum = cartRoot.TryGetProperty("order_discount_total", out var t)
            ? FormatDiscountMoney(t)
            : "";

        if (string.IsNullOrEmpty(pct) && string.IsNullOrEmpty(sum))
            return;

        var body = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(pct))
            body["order_discount_percent"] = pct;
        if (!string.IsNullOrEmpty(sum))
            body["order_discount_total"] = sum;

        if (body.Count == 0)
            return;

        if (UseSnapshotEditing || _cart.IsStaging || OfflineModeHelper.UseLocalOperations || _cart.IsLocalOffline)
        {
            body.TryGetValue("order_discount_percent", out pct);
            body.TryGetValue("order_discount_total", out sum);
            if (UseSnapshotEditing || _cart.IsStaging)
                ReceiptSnapshotCartEditor.PatchOrderDiscount(_cart, pct, sum);
            else
                LocalCartService.PatchOrderDiscount(_cart, pct, sum);
            return;
        }

        if (_cart.CanRefresh && App.Api != null)
            await ApplyOrderDiscountBodyAsync(body, "", showToast: false).ConfigureAwait(true);
    }

    private void ReturnToPreviousReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (!CanReturnToPreviousReceipt())
        {
            ShowToast("Нет предыдущего чека для возврата.", warn: true);
            return;
        }

        if (_receiptReturnTargetId == null)
            SwitchToPrimaryReceipt();
        else
            SwitchToDeferredReceipt(_receiptReturnTargetId);
    }

    private async Task RestoreDeferredCartsAsync(IReadOnlyList<DeferredCartEntry> entries, DeferredRestoreMode restoreMode)
    {
        if (App.Api == null || entries.Count == 0)
            return;

        if (restoreMode == DeferredRestoreMode.ReplaceCurrentCart &&
            entries.Count == 1)
        {
            await RestoreDeferredReceiptReplaceFastAsync(entries[0]).ConfigureAwait(true);
            return;
        }

        await RestoreDeferredCartsViaApiAsync(entries, restoreMode).ConfigureAwait(true);
    }

    /// <summary>Замена активного чека отложенным — только память, без API и без нового чека.</summary>
    private async Task RestoreDeferredReceiptReplaceFastAsync(DeferredCartEntry entry)
    {
        if (!await EnsureShiftReadyForOperationsAsync(false, CancellationToken.None).ConfigureAwait(true))
            return;

        var sw = Stopwatch.StartNew();
        PosLogger.Log($"RECEIPT restore replace fast: id={entry.Id}", "RECEIPT");

        if (HasActiveCartLines && _viewingDeferredEntryId == null)
        {
            if (!await DeferCurrentCartAsync(startNewSale: false, showToast: false).ConfigureAwait(true))
                return;
        }
        else
        {
            PersistCurrentReceiptSnapshot();
        }

        var snapshot = new OpenReceiptSnapshot
        {
            CartJson = OpenReceiptSnapshot.CloneCartJson(entry.CartJson),
            ServerCartId = null,
        };
        if (OfflineModeHelper.UseLocalOperations)
            snapshot.ApplyTo(_cart);
        else
            OpenReceiptSnapshot.ApplyDeferredStaging(_cart, entry.CartJson);
        _viewingDeferredEntryId = null;
        _primaryReceiptSnapshot = OpenReceiptSnapshot.Capture(_cart);
        SyncActiveReceiptContext();
        DeferredCartsStore.RemoveIds(new[] { entry.Id });

        RebindCartUi();
        UpdateDeferredCartUi();
        PosLogger.Log($"RECEIPT restore replace fast done in {sw.ElapsedMilliseconds} ms", "RECEIPT");
        ShowToast("Отложенный чек загружен.");
    }

    private async Task RestoreDeferredCartsViaApiAsync(IReadOnlyList<DeferredCartEntry> entries, DeferredRestoreMode restoreMode)
    {
        if (App.Api == null || entries.Count == 0)
            return;

        // ---------- Режим "Заменить текущий чек" ----------
        if (restoreMode == DeferredRestoreMode.ReplaceCurrentCart)
        {
            if (HasActiveCartLines &&
                !await DeferCurrentCartAsync(startNewSale: false, showToast: false).ConfigureAwait(true))
                return;

            if (!await EnsureShiftReadyForOperationsAsync(false, CancellationToken.None).ConfigureAwait(true))
                return;

            ForceClearCartDisplay();
            try
            {
                await TryStartNewSaleAsync(CancellationToken.None).ConfigureAwait(true);

                // Получаем актуальный cartId после старта новой корзины
                string? cartId = _cart.CartId;
                if (string.IsNullOrEmpty(cartId))
                {
                    PosMessageBox.Show("Не удалось определить корзину после старта продажи.",
                        "Отложенные", MessageBoxButton.OK, MessageBoxImage.Hand);
                    return;
                }

                // Удаляем всё, что могло остаться в новой корзине
                var items = CartDisplayHelper.EnumerateItems(_cart.Root).ToList();
                foreach (var it in items)
                {
                    var id = CartDisplayHelper.TryItemId(it);
                    if (!string.IsNullOrEmpty(id))
                        await App.SalesApi.PosCartItemDeleteAsync(cartId, id, CancellationToken.None);
                }
                await ReloadCartFromServerAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                PosMessageBox.Show("Не удалось открыть новую продажу: " + ex.Message,
                    "Отложенные", MessageBoxButton.OK, MessageBoxImage.Hand);
                return;
            }
        }
        // ---------- Корзина отсутствует (обычный режим) ----------
        else if (!_cart.HasCart || !_cart.CanRefresh)
        {
            if (!await EnsureShiftReadyForOperationsAsync(false, CancellationToken.None).ConfigureAwait(true))
                return;

            ForceClearCartDisplay();
            try
            {
                await TryStartNewSaleAsync(CancellationToken.None).ConfigureAwait(true);

                string? cartId = _cart.CartId;
                if (string.IsNullOrEmpty(cartId))
                {
                    PosMessageBox.Show("Не удалось определить корзину после старта продажи.",
                        "Отложенные", MessageBoxButton.OK, MessageBoxImage.Hand);
                    return;
                }

                var items = CartDisplayHelper.EnumerateItems(_cart.Root).ToList();
                foreach (var it in items)
                {
                    var id = CartDisplayHelper.TryItemId(it);
                    if (!string.IsNullOrEmpty(id))
                        await App.SalesApi.PosCartItemDeleteAsync(cartId, id, CancellationToken.None);
                }
                await ReloadCartFromServerAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                PosMessageBox.Show("Не удалось открыть новую продажу: " + ex.Message,
                    "Отложенные", MessageBoxButton.OK, MessageBoxImage.Hand);
                return;
            }
        }

        // ---------- Восстановление позиций ----------
        // Гарантируем наличие cartId перед восстановлением
        string? restoreCartId = _cart.CartId;
        if (string.IsNullOrEmpty(restoreCartId))
        {
            PosMessageBox.Show("Идентификатор корзины не определён.", "Отложенные", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        List<string> restoredIds = new List<string>();
        try
        {
            foreach (var hold in entries)
            {
                bool success = false;
                for (int attempt = 0; attempt < 2 && !success; attempt++)
                {
                    try
                    {
                        using (var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(hold.CartJson) ? "{}" : hold.CartJson))
                        {
                            foreach (var it in CartDisplayHelper.EnumerateItems(doc.RootElement))
                            {
                                var pid = CartDisplayHelper.TryProductId(it);
                                if (string.IsNullOrEmpty(pid))
                                    continue;

                                bool weighed = CartDisplayHelper.LineMustWeigh(it);
                                string qty = FormatQuantityForApi(CartDisplayHelper.LineQuantity(it), weighed);
                                string up = CartDisplayHelper.FormatMoney(CartDisplayHelper.UnitPrice(it));
                                string? disc = CartDisplayHelper.OptionalDiscountTotalParam(it);

                                var resp = await App.SalesApi.PosAddItemAsync(restoreCartId, pid, qty, up, disc, CancellationToken.None)
                                    .ConfigureAwait(true);
                                if (!CartResponseHelper.TryUpdateCartSession(resp, _cart))
                                    await ReloadCartFromServerAsync().ConfigureAwait(true);
                            }
                        }
                        success = true;
                    }
                    catch (ApiException) when (attempt == 0 && restoreMode == DeferredRestoreMode.ReplaceCurrentCart)
                    {
                        // Откатываемся на новую пустую корзину и пробуем ещё раз
                        try
                        {
                            await TryStartNewSaleAsync(CancellationToken.None).ConfigureAwait(true);
                            RebindCartUi();
                            // обновляем cartId после пересоздания корзины
                            restoreCartId = _cart.CartId;
                            if (string.IsNullOrEmpty(restoreCartId))
                            {
                                PosMessageBox.Show("Не удалось пересоздать корзину.", "Отложенные", MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }
                        }
                        catch
                        {
                            throw;
                        }
                    }
                }

                if (!success)
                {
                    PosMessageBox.Show("Не удалось восстановить чек «" + hold.Label + "».",
                        "Отложенные", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
                else
                {
                    restoredIds.Add(hold.Id);
                }
            }

            // Финальная перезагрузка корзины с сервера
            await ReloadCartFromServerAsync().ConfigureAwait(true);
            RebindCartUi();

            // Удаляем успешно восстановленные записи
            if (restoredIds.Count > 0)
            {
                DeferredCartsStore.RemoveIds(restoredIds);
                UpdateDeferredCartUi();
            }

            ShowToast($"Загружено отложенных корзин: {restoredIds.Count}.");
        }
        catch (ApiException ex)
        {
            PosMessageBox.Show(ex.Message, "Отложенные", MessageBoxButton.OK, MessageBoxImage.Hand);
        }
        catch (HttpRequestException ex)
        {
            PosMessageBox.Show(string.IsNullOrWhiteSpace(ex.Message) ? "Нет сети." : ex.Message,
                "Отложенные", MessageBoxButton.OK, MessageBoxImage.Hand);
        }
    }

    private void SyncDiscountFieldsFromCart()
    {
        // ❌ УДАЛИТЬ ЭТУ СТРОКУ
        // if (OrderDiscountSummaryText == null) return;

        if (!_cart.HasCart || _cart.Root.ValueKind != JsonValueKind.Object)
        {
            OrderDiscountSummaryText.Text = "Скидка не задана.";
            if (OrderDiscountButton != null)
                OrderDiscountButton.Visibility = Visibility.Collapsed;
            return;
        }

        var pct = GetCurrentOrderDiscountPercent();
        var sum = GetCurrentOrderDiscountSum();
        var hasDiscount = !string.IsNullOrEmpty(pct) || !string.IsNullOrEmpty(sum);
        OrderDiscountSummaryText.Text = !string.IsNullOrEmpty(pct)
            ? $"Скидка: {pct}%"
            : !string.IsNullOrEmpty(sum)
                ? $"Скидка: {sum} сом"
                : "Скидка не задана.";
        if (OrderDiscountButton != null)
            OrderDiscountButton.Visibility = hasDiscount ? Visibility.Visible : Visibility.Collapsed;
    }

    private string GetCurrentOrderDiscountPercent()
    {
        if (!_cart.HasCart || _cart.Root.ValueKind != JsonValueKind.Object)
            return "";

        var c = _cart.Root;
        return c.TryGetProperty("order_discount_percent", out var p) ? FormatDiscountScalar(p) : "";
    }

    private string GetCurrentOrderDiscountSum()
    {
        if (!_cart.HasCart || _cart.Root.ValueKind != JsonValueKind.Object)
            return "";

        var c = _cart.Root;
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

    private static Dictionary<string, string> BuildCheckoutRequestBody(
        string paymentMethod,
        string cashReceived,
        bool printReceipt)
    {
        var body = new Dictionary<string, string>
        {
            ["payment_method"] = paymentMethod ?? "",
            ["print_receipt"] = printReceipt ? "true" : "false",
            ["cash_received"] = cashReceived ?? "",
        };

        if (!string.IsNullOrWhiteSpace(App.PosCashboxId))
            body["cashbox_id"] = App.PosCashboxId.Trim();

        var shiftId = App.ActiveShiftId;
        if (!string.IsNullOrWhiteSpace(shiftId)
            && !shiftId.StartsWith("offline-", StringComparison.OrdinalIgnoreCase))
            body["shift_id"] = shiftId.Trim();

        return body;
    }

    private async Task<JsonElement> FinalizeReceiptAsync(string paymentMethod, string cashReceived, bool printReceipt)
    {
        EnsureCheckoutUsesActiveReceiptCart();

        var cartId = _cart.CartId;
        if (string.IsNullOrWhiteSpace(cartId))
            throw new ApiException("Корзина не привязана к серверу. Начните продажу заново.", 400);

        var body = BuildCheckoutRequestBody(paymentMethod, cashReceived, printReceipt);
        var checkoutIds = CartDisplayHelper.CollectCheckoutTargetIds(_cart.Root, cartId);
        PosLogger.Log(
            $"Checkout API: ids=[{string.Join(", ", checkoutIds)}], method={body.GetValueOrDefault("payment_method")}, " +
            $"cash_received={body.GetValueOrDefault("cash_received")}, shift={body.GetValueOrDefault("shift_id") ?? "—"}",
            "PAYMENT");

        var checkoutResponse = await App.SalesApi.PosCheckoutAsync(checkoutIds, body, _windowCts.Token)
            .ConfigureAwait(true);
        CheckoutResponseHelper.FormatSuccess(checkoutResponse);

        var saleId = CheckoutResponseHelper.TrySaleId(checkoutResponse) ?? cartId;
        var cartSnapshot = _cart.Root.Clone();
        var saleLines = CartDisplayHelper.EnumerateItems(cartSnapshot)
            .Select(it =>
            {
                var productId = CartDisplayHelper.TryProductId(it);
                var qty = CartDisplayHelper.LineQuantity(it);
                return string.IsNullOrEmpty(productId) ? null : new Core.Domain.CartLineDto(productId, qty);
            })
            .Where(line => line != null)
            .Cast<Core.Domain.CartLineDto>()
            .ToList();

        if (saleLines.Count > 0)
        {
            _ = _mediator.Publish(
                new Core.Application.Notifications.SaleFinalizedNotification(saleId, saleLines),
                CancellationToken.None);
        }

        _ = Task.Run(async () =>
        {
            await StockSyncService.RefreshSoldItemsStockAsync(cartSnapshot, CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                RestoreTilesFromCache();
                ApplyCatalogViewport();
            });
        });

        var total = CartTotalsCalculator.Calculate(cartSnapshot).TotalDue;
        App.AuditDb.LogSale(saleId, total, paymentMethod ?? "", App.CurrentUserId);

        _ = Task.Run(() => App.OfflineSync.TriggerSyncNowAsync(CancellationToken.None));

        return checkoutResponse;
    }

    private Task RefreshShiftStateAsync(CancellationToken cancellationToken = default) =>
        _shiftStateService.RefreshAsync(cancellationToken);

    /// <summary>
    /// Смена должна быть открыта на кассе. Делегирует проверку в Core (PosSessionService).
    /// </summary>
    private Task<bool> EnsureShiftReadyForOperationsAsync(bool silent, CancellationToken cancellationToken = default)
    {
        async Task<bool> CoreAsync()
        {
            var ok = await _posSessionService.EnsureOperationalAsync(cancellationToken, silent).ConfigureAwait(true);
            if (!ok && !silent)
            {
                const string shortMsg = "Смена не открыта — нажмите «Открыть смену» в шапке.";
                CartMessageText.Text = shortMsg;
                CartMessageText.Foreground = UiWarn;
                PosLogger.Log("Операция отклонена: смена не открыта", "SHIFT");
            }

            return ok;
        }

        if (Dispatcher.CheckAccess())
            return CoreAsync();

        return Dispatcher.InvokeAsync(CoreAsync, DispatcherPriority.Normal).Task.Unwrap();
    }

    private async void ClearOrderDiscountFromHeader_Click(object sender, RoutedEventArgs e)
    {
        await ClearOrderDiscountAsync();
    }
    private void UpdateShiftBanner()
    {
        bool shiftOpen = !string.IsNullOrEmpty(App.ActiveShiftId);
        ShiftOpenForSale = shiftOpen;

        if (OpenShiftButton != null)
            OpenShiftButton.IsEnabled = !shiftOpen;

        if (CloseShiftButton != null)
            CloseShiftButton.IsEnabled = shiftOpen;

        if (ShiftBalanceText != null)
        {
            ShiftBalanceText.Text = shiftOpen
                ? $"Касса: {ShiftBalanceHelper.FormatBalance(_shiftCashBalance)}"
                : "Касса: 0.00 сом";
        }

        if (HamburgerShiftBalanceText != null)
        {
            HamburgerShiftBalanceText.Text = shiftOpen
                ? ShiftBalanceHelper.FormatBalance(_shiftCashBalance)
                : "Смена не открыта";
        }
    }

    private async void OpenShift_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenShiftDialog { SuggestedBalance = _shiftCashBalance };
        if (PosDialogHost.Show(dlg, this) == true)
        {
            await OpenShiftWithCashAsync(dlg.OpeningCash);
        }
    }

    private async void CloseShift_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(App.ActiveShiftId)) return;

        var dlg = new CloseShiftDialog { SuggestedBalance = _shiftCashBalance };
        if (PosDialogHost.Show(dlg, this) == true)
        {
            await CloseShiftWithCashAsync(dlg.ClosingCash);
        }
    }

    private async Task<string?> EnsurePosCashboxIdAsync(CancellationToken cancellationToken = default)
    {
        var cb = App.PosCashboxId;
        if (!string.IsNullOrWhiteSpace(cb))
            return cb;
        var rawList = await App.ShiftApi.ConstructionCashboxesListAsync(cancellationToken).ConfigureAwait(true);
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
            var answer = PosMessageBox.Show(
                this,
                "В текущем чеке есть товары. Очистить список и открыть новый чек?",
                "Новый чек",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        await StartNewSaleCoreAsync().ConfigureAwait(true);
    }

    private async Task StartNewSaleCoreAsync()
    {
        if (!await EnsureShiftReadyForOperationsAsync(silent: false).ConfigureAwait(true))
            return;

        if (IsViewingDeferredReceipt)
            PersistCurrentReceiptSnapshot();
        _viewingDeferredEntryId = null;
        ForceClearCartDisplay();
        CartMessageText.Text = "Создаётся новый чек…";
        CartMessageText.Foreground = UiMuted;
        StartSaleButton.IsEnabled = false;

        try
        {
            await TryStartNewSaleAsync().ConfigureAwait(true);

            if (_cart.IsStaging)
            {
                RebindCartUi();
                CapturePrimaryReceiptSnapshot();
                CartMessageText.Text = "Новый чек открыт.";
                CartMessageText.Foreground = UiOk;
                ShowToast("Готов к продаже", false);
                return;
            }

            var items = CartDisplayHelper.EnumerateItems(_cart.Root).ToList();
            foreach (var it in items)
            {
                var id = CartDisplayHelper.TryItemId(it);
                if (!string.IsNullOrEmpty(id))
                    await App.SalesApi.PosCartItemDeleteAsync(_cart.CartId!, id, CancellationToken.None);
            }
            await ReloadCartFromServerAsync().ConfigureAwait(true);

            RebindCartUi();
            CapturePrimaryReceiptSnapshot();
            CartMessageText.Text = "Новый чек открыт.";
            CartMessageText.Foreground = UiOk;
            ShowToast("Готов к продаже", false);
        }
        catch (Exception ex)
        {
            CartMessageText.Text = ex is ApiException ? ex.Message : "Ошибка связи с сервером.";
            CartMessageText.Foreground = UiWarn;
            ShowToast("Не удалось открыть новый чек", true);
        }
        finally
        {
            StartSaleButton.IsEnabled = true;
        }
    }

    private async Task TryStartNewSaleAsync(CancellationToken cancellationToken = default)
    {
        if (OfflineModeHelper.CanOperateWithoutServer)
        {
            LocalCartService.StartNewLocalCart(_cart);
            return;
        }

        var deferredCartIds = GetDeferredServerCartIds();
        var cb = await EnsurePosCashboxIdAsync(cancellationToken).ConfigureAwait(true);

        try
        {
            await CartSaleSessionHelper
                .StartNewSaleAsync(App.SalesApi, _cart, cb, cancellationToken)
                .ConfigureAwait(true);

            var cartId = _cart.CartId;
            if (deferredCartIds.Count > 0
                && !string.IsNullOrWhiteSpace(cartId)
                && deferredCartIds.Contains(cartId))
            {
                PosLogger.Log(
                    $"RECEIPT: sales/start вернул отложенную корзину {cartId}; открываем локальный черновик",
                    "RECEIPT");
                _cart.Clear();
                StagingCartService.StartEmpty(_cart);
                return;
            }

            if (_cart.CanRefresh)
                await ReloadCartFromServerAsync().ConfigureAwait(true);
        }
        catch (ApiException ex) when (deferredCartIds.Count > 0)
        {
            PosLogger.Log(
                $"RECEIPT: sales/start недоступен ({ex.Message}); открываем локальный черновик",
                "RECEIPT");
            _cart.Clear();
            StagingCartService.StartEmpty(_cart);
        }
    }

    /// <summary>После успешной оплаты: сброс локальной корзины и автоматическое открытие нового чека.</summary>
    /// <returns>null при успехе; иначе краткий текст ошибки для пользователя.</returns>
    private async Task<string?> TryRestartSaleSessionAfterCheckoutAsync()
    {
        if (_viewingDeferredEntryId != null)
        {
            DeferredCartsStore.RemoveIds(new[] { _viewingDeferredEntryId });
            _viewingDeferredEntryId = null;
        }

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
            CapturePrimaryReceiptSnapshot();
            return null;
        }
        catch (ApiException ex)
        {
            if (OfflineModeHelper.UseLocalOperations)
            {
                LocalCartService.StartNewLocalCart(_cart);
                RebindCartUi();
                CapturePrimaryReceiptSnapshot();
                return null;
            }

            RebindCartUi();
            PosLogger.Log($"После оплаты: не удалось начать продажу (API): {ex.Message}", "PAYMENT");
            return ex.Message;
        }
        catch (HttpRequestException ex)
        {
            if (OfflineModeHelper.UseLocalOperations || OfflineModeHelper.CanOperateWithoutServer)
            {
                LocalCartService.StartNewLocalCart(_cart);
                RebindCartUi();
                CapturePrimaryReceiptSnapshot();
                return null;
            }

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
        if (e.Key is Key.Enter or Key.Return)
        {
            if (TryHandleBarcodeEnter(e))
                return;

            if (TryTriggerCheckoutFromEnter(e))
                return;

            return;
        }

        _barcodeInputService.ProcessKeyDown(e);
    }

    /// <summary>Enter в поле штрихкода или от HID-сканера — только добавление товара, не оплата.</summary>
    private bool TryHandleBarcodeEnter(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox barcodeBox && ReferenceEquals(barcodeBox, BarcodeBox))
        {
            e.Handled = true;
            var code = BarcodeBox.Text.Trim();
            if (code.Length > 0)
                _ = RunScanAsync(code);
            else
                BarcodeBox.Clear();
            return true;
        }

        // Поиск по каталогу обрабатывает своё поле в CatalogSearchBox_KeyDown.
        if (Keyboard.FocusedElement is TextBox searchBox && ReferenceEquals(searchBox, CatalogSearchBox))
            return false;

        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox)
            return false;

        // HID-сканер: сначала разбираем Enter как завершение штрихкода, не как «Оплатить».
        _barcodeInputService.ProcessKeyDown(e);
        return e.Handled;
    }

    private bool TryTriggerCheckoutFromEnter(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox)
            return false;

        if (!string.IsNullOrWhiteSpace(BarcodeBox.Text))
            return false;

        if (!string.IsNullOrWhiteSpace(CatalogSearchBox.Text))
            return false;

        if (CheckoutFooterButton is not { IsEnabled: true, Visibility: Visibility.Visible })
            return false;

        e.Handled = true;
        Checkout_Click(this, new RoutedEventArgs(Button.ClickEvent, CheckoutFooterButton));
        return true;
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
                PosMessageBox.Show(
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
            PosMessageBox.Show("Подключение не готово.", "Оплата", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!HasCheckoutableCart())
        {
            PosLogger.Log("Оплата: нет корзины или пусто", "PAYMENT");
            PosMessageBox.Show("Добавьте товары в корзину.", "Оплата", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!await EnsureShiftReadyForOperationsAsync(false).ConfigureAwait(true))
            return;

        if (TryBlockCheckoutForStockIssues())
            return;

        try
        {
            if (IsViewingDeferredReceipt)
                await EnsureDeferredCartReadyForCheckoutAsync().ConfigureAwait(true);
            else if (!_cart.IsLocalOffline && !OfflineModeHelper.CanOperateWithoutServer)
                await EnsureActiveCartReadyForCheckoutAsync().ConfigureAwait(true);
        }
        catch (ApiException ex)
        {
            PosMessageBox.Show(ex.Message, "Оплата", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var totals = CartTotalsCalculator.Calculate(_cart.Root);
        var dlg = new CheckoutDialog(
            totals,
            GetCurrentOrderDiscountPercent(),
            GetCurrentOrderDiscountSum())
        { Owner = this };
        if (dlg.ShowDialog() != true)
        {
            PosLogger.Log("Оплата: диалог отменён", "PAYMENT");
            return;
        }

        if (dlg.PendingOrderDiscountBody != null)
        {
            if (!await TryApplyCheckoutDiscountAsync(dlg.PendingOrderDiscountBody).ConfigureAwait(true))
            {
                PosMessageBox.Show(
                    PaymentErrorMessages.DiscountFailure,
                    "Оплата",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        var total = CartDisplayHelper.TotalDue(_cart.Root);
        var paymentMethod = dlg.PaymentMethodKey;
        var cashReceived = dlg.CashReceivedForApi;
        var printReceipt = dlg.IsPrintReceiptEnabled;

        PosLogger.Log(
            $"Диалог оплаты OK: method={paymentMethod}, cash_received={cashReceived}, total={total}, print={printReceipt}",
            "PAYMENT");

        if (string.IsNullOrWhiteSpace(_cart.CartId) && !_cart.IsLocalOffline)
        {
            PosLogger.Log("Оплата: CartId пуст после диалога", "PAYMENT");
            PosMessageBox.Show(
                "Корзина не готова к оплате. Нажмите «Новый чек» и добавьте товары снова.",
                "Оплата",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        CartMessageText.Text = "";
        SetScanBusy(true);
        var cartJsonSnapshot = _cart.Root.GetRawText();

        if (OfflineModeHelper.UseLocalOperations || _cart.IsLocalOffline)
        {
            try
            {
                await CompleteOfflineCheckoutAsync(paymentMethod, cashReceived, cartJsonSnapshot, printReceipt, networkLost: false)
                    .ConfigureAwait(true);
            }
            finally
            {
                SetScanBusy(false);
            }

            return;
        }

        try
        {
            var checkoutResponse = await FinalizeReceiptAsync(paymentMethod, cashReceived, printReceipt)
                .ConfigureAwait(true);
            var postCheckoutMessage = await CompleteOnlineCheckoutUiAsync(
                total,
                paymentMethod,
                cashReceived,
                cartJsonSnapshot,
                printReceipt,
                checkoutResponse).ConfigureAwait(true);

            if (postCheckoutMessage != null)
            {
                PosMessageBox.Show(
                    postCheckoutMessage,
                    "Оплата",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (ApiException ex)
        {
            await RecoverCartAfterFailedCheckoutAsync(cartJsonSnapshot, ex).ConfigureAwait(true);
            PaymentErrorMessages.Log("Оплата ApiException", ex);
            PosMessageBox.Show(PaymentErrorMessages.ForCashier(ex), "Оплата", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (HttpRequestException ex)
        {
            await RecoverCartAfterFailedCheckoutAsync(cartJsonSnapshot, ex).ConfigureAwait(true);
            PaymentErrorMessages.Log("Оплата HttpRequestException", ex);
            await HandleAutomaticOfflineCheckoutAsync(paymentMethod, cashReceived, printReceipt, ex.Message).ConfigureAwait(true);
        }
        catch (JsonException ex)
        {
            await RecoverCartAfterFailedCheckoutAsync(cartJsonSnapshot, ex).ConfigureAwait(true);
            PaymentErrorMessages.Log("Оплата JsonException", ex);
            PosMessageBox.Show(PaymentErrorMessages.GenericFailure, "Оплата", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (OperationCanceledException ex)
        {
            await RecoverCartAfterFailedCheckoutAsync(cartJsonSnapshot, ex).ConfigureAwait(true);
            PaymentErrorMessages.Log("Оплата отмена/таймаут", ex);
            await HandleAutomaticOfflineCheckoutAsync(paymentMethod, cashReceived, printReceipt, "Таймаут оплаты или потеря сети.")
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await RecoverCartAfterFailedCheckoutAsync(cartJsonSnapshot, ex).ConfigureAwait(true);
            PaymentErrorMessages.Log("Оплата Exception", ex);
            PosMessageBox.Show(PaymentErrorMessages.GenericFailure, "Оплата", MessageBoxButton.OK, MessageBoxImage.Error);
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
        _cart.Clear();
        OrderDiscountSummaryText.Text = "Скидка не задана.";
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        CatalogSearchBox.Text = "";
    }

    private void OnCatalogCacheChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        // При любом изменении кэша полностью перестраиваем каталог.
        UpdateCatalogDisplay();
    }

    private void UpdateCatalogDisplay()
    {
        CatalogProductClassifier.SplitIntoCatalogLists(CatalogCacheService.Products, _allTilesKg, _allTilesPiece);

        SortByFavorite();
        ApplyCatalogViewport();
        UpdateCatalogCount();
        OnPropertyChanged(nameof(CatalogTableSource));
    }

    private async Task HandleAutomaticOfflineCheckoutAsync(
        string? paymentMethod,
        string? cashReceived,
        bool printReceipt,
        string? reason)
    {
        try
        {
            await CompleteOfflineCheckoutAsync(
                paymentMethod,
                cashReceived,
                _cart.Root.GetRawText(),
                printReceipt,
                networkLost: true,
                reason).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            PaymentErrorMessages.Log("Критическая ошибка офлайн-сохранения", ex);
            PosMessageBox.Show(
                "Связь с сервером прервалась, и чек не удалось сохранить в офлайн-очередь.\n\n" +
                "Корзина не очищена. Повторите сохранение или вызовите администратора.",
                "Оплата",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task CompleteOfflineCheckoutAsync(
        string? paymentMethod,
        string? cashReceived,
        string cartJsonSnapshot,
        bool printReceipt,
        bool networkLost,
        string? reason = null)
    {
        var saved = SaveCurrentSaleOffline(paymentMethod, cashReceived);
        PosLogger.Log($"Оплата сохранена офлайн: {saved.Id}. Причина: {reason ?? "офлайн-режим"}", "PAYMENT");

        if (printReceipt)
        {
            CartMessageText.Text = "Печать чека...";
            CartMessageText.Foreground = UiOk;
            await TryPrintReceiptSafeAsync(
                cartJsonSnapshot,
                offlineNote: "ОФФЛАЙН (ожидает выгрузку)",
                paymentMethodKey: paymentMethod,
                cashReceived: cashReceived).ConfigureAwait(true);
        }

        var restartErr = await TryRestartSaleSessionAfterCheckoutAsync().ConfigureAwait(true);
        if (restartErr != null)
            throw new InvalidOperationException(
                "Чек сохранён офлайн, но не удалось автоматически открыть новый чек. " +
                "Корзина не очищена. " + restartErr);

        CartMessageText.Text = "Оффлайн-чек сохранён. Новый чек открыт.";
        CartMessageText.Foreground = UiWarn;

        if (networkLost)
        {
            PosMessageBox.Show(
                "Связь пропала во время оплаты.\n\n" +
                "Чек автоматически сохранён офлайн и будет обработан после восстановления связи.",
                "Оффлайн-оплата",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else
        {
            ShowToast($"Чек в очереди синхронизации ({OfflinePendingSalesStore.PendingCount}).", warn: false);
        }

        _ = App.OfflineSync.TriggerSyncNowAsync(CancellationToken.None);
    }

    private async Task<string?> CompleteOnlineCheckoutUiAsync(
        double totalAmount,
        string? paymentMethod,
        string? cashReceived,
        string cartJsonSnapshot,
        bool printReceipt,
        JsonElement checkoutResponse)
    {
        string? printWarning = null;
        if (printReceipt)
        {
            CartMessageText.Text = "Печать чека...";
            CartMessageText.Foreground = UiOk;
            var printed = await TryPrintReceiptSafeAsync(
                cartJsonSnapshot,
                paymentMethodKey: paymentMethod,
                cashReceived: cashReceived,
                checkoutResponse: checkoutResponse).ConfigureAwait(true);
            if (!printed)
                printWarning = "Печать чека не удалась. Проверьте принтер.";
        }

        var restartErr = await TryRestartSaleSessionAfterCheckoutAsync().ConfigureAwait(true);
        await RefreshShiftStateAsync().ConfigureAwait(true);

        if (restartErr != null)
        {
            CartMessageText.Text = "Оплата прошла. Нажмите «+ Новый чек», чтобы продолжить.";
            CartMessageText.Foreground = UiWarn;
            return printWarning == null
                ? $"Оплата выполнена на сервере. {restartErr}"
                : $"Оплата выполнена на сервере, но {printWarning} {restartErr}";
        }

        if (printWarning != null)
        {
            CartMessageText.Text = "Оплата успешно завершена. Новый чек открыт.";
            CartMessageText.Foreground = UiOk;
            return $"Оплата выполнена на сервере, но {printWarning} Новый чек открыт.";
        }

        CartMessageText.Text = "Оплата успешно завершена. Новый чек открыт.";
        CartMessageText.Foreground = UiOk;
        PosMessageBox.Show(
            $"Оплата завершена успешно.\nСумма: {totalAmount:0.00} сом.",
            "Оплата",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return null;
    }

    /// <summary>
    /// После сбоя checkout (чек не проведён): вернуть редактируемое состояние корзины из снимка.
    /// </summary>
    private async Task RecoverCartAfterFailedCheckoutAsync(string cartJsonSnapshot, Exception error)
    {
        try
        {
            if (error is ApiException apiEx
                && apiEx.StatusCode == 404
                && CartResponseHelper.LooksLikeStaleCart(apiEx))
            {
                PosLogger.Log(
                    "Checkout 404: корзина на сервере уже закрыта — открываем новый чек.",
                    "PAYMENT");
                try
                {
                    await TryStartNewSaleAsync().ConfigureAwait(true);
                    RebindCartUi();
                    CapturePrimaryReceiptSnapshot();
                    CartMessageText.Text = "Предыдущий чек уже закрыт на сервере. Открыт новый чек.";
                    CartMessageText.Foreground = UiWarn;
                    return;
                }
                catch (Exception restartEx)
                {
                    PosLogger.Log($"Checkout recovery: {restartEx.Message}", "PAYMENT");
                }
            }

            RestoreEditableCartSnapshot(cartJsonSnapshot);

            if (_cart.CanRefresh && !string.IsNullOrEmpty(_cart.CartId))
            {
                try
                {
                    await ReloadCartFromServerAsync().ConfigureAwait(true);
                    RebindCartUi();
                    CartMessageText.Text = "Оплата не выполнена. Чек можно отредактировать или оплатить снова.";
                    CartMessageText.Foreground = UiWarn;
                    return;
                }
                catch (ApiException reloadEx) when (CartResponseHelper.LooksLikeStaleCart(reloadEx))
                {
                    PosLogger.Log($"Checkout recovery: серверная корзина недоступна — локальный снимок.", "PAYMENT");
                    RestoreEditableCartSnapshot(cartJsonSnapshot, forceStaging: true);
                }
            }

            RebindCartUi();
            CartMessageText.Text = "Оплата не выполнена. Чек можно отредактировать или оплатить снова.";
            CartMessageText.Foreground = UiWarn;
        }
        catch (Exception ex)
        {
            PosLogger.Log($"Checkout recovery failed: {ex.Message}", "PAYMENT ERROR");
            try
            {
                RestoreEditableCartSnapshot(cartJsonSnapshot, forceStaging: true);
                RebindCartUi();
            }
            catch
            {
                /* последняя линия защиты — UI разблокируется в finally через SetScanBusy */
            }
        }
        finally
        {
            SetScanBusy(false);
        }
    }

    private void RestoreEditableCartSnapshot(string cartJsonSnapshot, bool forceStaging = false)
    {
        var json = string.IsNullOrWhiteSpace(cartJsonSnapshot) ? "{}" : cartJsonSnapshot;
        if (!forceStaging)
        {
            if (!CartJsonHelper.TryParseObject(json, out var root)
                || !CartJsonHelper.TryApplyObjectToCart(_cart, root))
            {
                _cart.Clear();
            }

            return;
        }

        CartJsonHelper.ApplyStagingToCart(_cart, json);
    }

    private void SyncActiveReceiptContext()
    {
        var cartId = _cart.IsStaging ? null : _cart.CartId;
        App.SalesApi?.SetActiveCartId(cartId);

        if (_viewingDeferredEntryId == null)
        {
            PosLogger.Log(
                $"RECEIPT context: primary cartId={cartId ?? "—"} staging={_cart.IsStaging}",
                "RECEIPT");
            return;
        }

        var entry = DeferredCartsStore.TryGetById(_viewingDeferredEntryId);
        PosLogger.Log(
            $"RECEIPT context: deferred={_viewingDeferredEntryId} cartId={cartId ?? "—"} " +
            $"staging={_cart.IsStaging} savedServerId={entry?.ServerCartId ?? "—"}",
            "RECEIPT");
    }

    private void EnsureCheckoutUsesActiveReceiptCart()
    {
        if (_cart.IsStaging)
            throw new ApiException(
                "Чек не синхронизирован с сервером. Повторите подготовку к оплате.",
                409);

        var cartId = _cart.CartId;
        if (string.IsNullOrWhiteSpace(cartId))
            throw new ApiException("Корзина не привязана к серверу. Начните продажу заново.", 400);

        var activeCartId = App.SalesApi?.ActiveCartId;
        if (!string.IsNullOrWhiteSpace(activeCartId)
            && !string.Equals(activeCartId, cartId, StringComparison.OrdinalIgnoreCase))
        {
            App.SalesApi?.SetActiveCartId(cartId);
            PosLogger.Log(
                $"Checkout: active cart context corrected {activeCartId} → {cartId}",
                "PAYMENT");
        }

        if (IsViewingDeferredReceipt)
        {
            var entry = DeferredCartsStore.TryGetById(_viewingDeferredEntryId!);
            if (entry != null
                && !string.IsNullOrWhiteSpace(entry.ServerCartId)
                && !string.Equals(entry.ServerCartId, cartId, StringComparison.OrdinalIgnoreCase))
            {
                PosLogger.Log(
                    $"Checkout: deferred entry server id={entry.ServerCartId} != active cartId={cartId}",
                    "PAYMENT");
            }
        }

        App.SalesApi?.SetActiveCartId(cartId);
        PosLogger.Log(
            $"Checkout cart guard: deferred={IsViewingDeferredReceipt} cartId={cartId}",
            "PAYMENT");
    }

    private void OfflineStatus_Click(object sender, RoutedEventArgs e)
    {
        int pending = OfflinePendingSalesStore.PendingCount;
        int failed = OfflinePendingSalesStore.FailedCount;
        string path = OfflineDatabase.DatabasePath;
        PosMessageBox.Show(
            $"Ожидают синхронизации: {pending}\nОшибок синхронизации: {failed}\n\nФайл данных:\n{path}",
            "Оффлайн-продажи", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        var filterWindow = new FilterWindow
        {
            Owner = this
        };

        if (filterWindow.ShowDialog() == true)
        {
            if (filterWindow.FullCatalogReloaded)
                _currentFilter = null;
            else if (filterWindow.Result != null)
                _currentFilter = filterWindow.Result;
            else
                _currentFilter = null;

            ResetAllProductsPage();
            _catalogVisibleLimitKg = CatalogPageSize;
            _catalogVisibleLimitPiece = CatalogPageSize;
            UpdateCatalogDisplay();
            if (filterWindow.FullCatalogReloaded)
                ShowToast("Фильтр сброшен — показан полный каталог.");
            else if (filterWindow.Result != null)
                ShowToast($"Фильтр: {CatalogCacheService.Products.Count} товаров.");
        }
    }

    private async void RefreshCart_Click(object sender, RoutedEventArgs e)
    {
        if (!_cart.CanRefresh)
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
            _cart.Clear();
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
        if (!_cart.CanRefresh)
            return;
        var c = await App.SalesApi.PosCartGetAsync(_cart.CartId!).ConfigureAwait(true);
        _cart.SetCart(c);
    }

    private void BarcodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Handled)
            return;

        e.Handled = true;
        _ = RunScanAsync(BarcodeBox.Text);
    }

    private async void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CatalogProductTileVm vm })
            return;

        var newState = !vm.IsFavorite;
        vm.IsFavorite = newState;

        CatalogCacheService.SetFavorite(vm.Id, newState);

        SortByFavorite();
        ApplyCatalogViewport();

        try
        {
            var synced = await App.CatalogApi.SetProductFavoriteAsync(vm.Id, newState, _windowCts.Token).ConfigureAwait(true);
            if (synced)
            {
                App.AuditDb.LogFavorite(vm.Id, newState);
                ShowToast(newState ? "Добавлено в избранное на сайте" : "Убрано из избранного на сайте");
            }
            else
            {
                ShowToast("Избранное сохранено локально (сайт не ответил)", warn: true);
            }
        }
        catch (ApiException ex)
        {
            ShowToast($"Синхронизация избранного: {ex.Message}", warn: true);
        }
        catch (HttpRequestException)
        {
            ShowToast("Избранное сохранено локально (нет сети)", warn: true);
        }
    }

    private void ApplyFilterToCatalog(FilterCriteria criteria)
    {
        _currentFilter = criteria;
        _catalogViewModel.ActiveFilter = criteria;
        SortByFavorite();
        _catalogVisibleLimitKg = CatalogPageSize;
        _catalogVisibleLimitPiece = CatalogPageSize;
        ResetAllProductsPage();
        ApplyCatalogViewport();
        if (GetSelectedCatalogTab() == CatalogTabKind.All)
            _ = LoadAllProductsPageAsync(append: false);
    }

    private bool FilterPredicate(object obj)
    {
        return obj is CatalogProductTileVm vm && _catalogViewModel.FilterPredicate(vm);
    }

    private void SortByFavorite()
    {
        // Сортируем: сначала избранные, потом по алфавиту
        Comparison<CatalogProductTileVm> comparison = (a, b) =>
        {
            int favCompare = b.IsFavorite.CompareTo(a.IsFavorite);
            if (favCompare != 0) return favCompare;
            return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
        };

        _allTilesKg.Sort(comparison);
        _allTilesPiece.Sort(comparison);
        ApplyCatalogViewport();
    }

    private void UpdateCatalogCount()
    {
        if (CatalogCountText == null)
            return;

        var tab = GetSelectedCatalogTab();
        if (!string.IsNullOrEmpty(_catalogSearchFilter))
        {
            var shown = tab switch
            {
                CatalogTabKind.All => _tilesAll.Count,
                CatalogTabKind.Piece => _tilesPiece.Count,
                _ => _tilesKg.Count,
            };
            CatalogCountText.Text = $"Показано {shown}";
            CatalogCountText.Visibility = Visibility.Visible;
            return;
        }

        if (tab == CatalogTabKind.All)
        {
            CatalogCountText.Text = _allProductsHasMore
                ? $"Показано {_tilesAll.Count}"
                : $"Показано {_tilesAll.Count} (все)";
            CatalogCountText.Visibility = Visibility.Visible;
            return;
        }

        var kgTotal = GetFilteredSourceCount(_allTilesKg);
        var pieceTotal = GetFilteredSourceCount(_allTilesPiece);
        var isPiece = tab == CatalogTabKind.Piece;
        CatalogCountText.Text = isPiece
            ? $"Показано {_tilesPiece.Count} из {pieceTotal}"
            : $"Показано {_tilesKg.Count} из {kgTotal}";
        CatalogCountText.Visibility = Visibility.Visible;
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

        await ProcessBarcodeScanAsync(code).ConfigureAwait(true);
    }

    private async Task ApplyLineDiscountAsync(string itemId, string? mode, string? value)
    {
        if (string.IsNullOrEmpty(itemId) || !_cart.HasCart)
            return;
        if (!UseSnapshotEditing && !_cart.CanRefresh && !_cart.IsStaging && !_cart.IsLocalOffline)
            return;

        SetScanBusy(true);
        try
        {
            if (UseSnapshotEditing)
            {
                ReceiptSnapshotCartEditor.PatchLineDiscount(_cart, itemId, mode, value);
                CartMessageText.Text = "Скидка на товар обновлена.";
                CartMessageText.Foreground = UiOk;
                AfterDeferredCartMutation();
                return;
            }

            if (OfflineModeHelper.UseLocalOperations || _cart.IsLocalOffline)
            {
                LocalCartService.PatchLineDiscount(_cart, itemId, mode, value);
                RebindCartUi();
                CartMessageText.Text = "Скидка на товар обновлена (офлайн).";
                CartMessageText.Foreground = UiOk;
                return;
            }

            var body = new Dictionary<string, string>();
            if (mode == null)
            {
                body["discount_percent"] = "0";
                body["discount_total"] = "0";
            }
            else if (mode == "percent")
            {
                body["discount_percent"] = value!;
            }
            else
            {
                body["discount_total"] = value!;
            }

            var resp = await App.SalesApi.PosCartItemPatchAsync(_cart.CartId!, itemId, body);
            if (!CartResponseHelper.TryUpdateCartSession(resp, _cart))
                await ReloadCartFromServerAsync();
            RebindCartUi();
            CartMessageText.Text = "Скидка на товар обновлена.";
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
        finally
        {
            SetScanBusy(false);
        }
    }

    private async void CartLineDiscount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not CartLineRow row)
            return;

        // Открываем диалог с предустановками для товара
        var dlg = new OrderDiscountDialog("", "");
        dlg.SetItemMode(row.Title, row.DiscountType, row.DiscountValue);
        if (ShowModalDialog(dlg) != true)
            return;

        if (dlg.ClearRequested)
        {
            await ApplyLineDiscountAsync(row.ItemId, null, null); // сброс
        }
        else
        {
            await ApplyLineDiscountAsync(row.ItemId, dlg.DiscountMode, dlg.DiscountValue);
        }
    }

    internal void SetScanBusy(bool busy)
    {
        _isUiBusy = busy;
        var can = _cart.CanRefresh || UseSnapshotEditing || _cart.IsLocalOffline;
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
            RestoreLatestDeferredButton.IsEnabled = !busy && CanReturnToPreviousReceipt();
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
        if (string.IsNullOrEmpty(row.ItemId) || !_cart.HasCart)
            return;
        if (!UseSnapshotEditing && !_cart.CanRefresh && !_cart.IsStaging && !_cart.IsLocalOffline)
            return;
        await DeleteLineAsync(row.ItemId).ConfigureAwait(true);
    }

    private async void CartWeigh_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not CartLineRow row || !row.WeighedLine)
            return;
        if (string.IsNullOrEmpty(row.ItemId) || !_cart.HasCart)
            return;
        if (!UseSnapshotEditing && !_cart.CanRefresh && !_cart.IsStaging && !_cart.IsLocalOffline)
            return;

        var initial = FormatQtySubline(row.Qty, true);
        var dlg = new WeighedProductDialog(
            row.Title,
            row.PricePerKgHint,
            GetScaleForWeighDialog(),
            initialKg: initial,
            okButtonText: "Применить",
            windowTitle: "Изменить вес")
        { Owner = this };

        if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.QuantityNormalized))
            return;

        if (!double.TryParse(dlg.QuantityNormalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedWeight) || parsedWeight <= 0)
            return;

        var weight = JsonNumericReader.RoundWeight(parsedWeight);
        var weightForApi = JsonNumericReader.FormatWeightForApi(weight);

        SetScanBusy(true);
        try
        {
            if (UseSnapshotEditing)
            {
                LogCartOpDiagnostics("WEIGH", row.ItemId);
                ReceiptSnapshotCartEditor.UpdateLineQuantity(_cart, row.ItemId, weight);
                CartMessageText.Text = "";
                AfterDeferredCartMutation();
                return;
            }

            var resp = await App.SalesApi
                .PosCartItemPatchAsync(
                    _cart.CartId!,
                    row.ItemId,
                    new Dictionary<string, string> { ["quantity"] = weightForApi })
                .ConfigureAwait(true);
            if (!CartResponseHelper.TryUpdateCartSession(resp, _cart))
                CartInPlaceRecalculator.UpdateLineQuantity(_cart, row.ItemId, weight, weighed: true);
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
        if (string.IsNullOrEmpty(row.ItemId) || !_cart.HasCart)
            return;
        if (!UseSnapshotEditing && !_cart.CanRefresh && !_cart.IsStaging)
            return;

        var currentQty = row.WeighedLine
            ? JsonNumericReader.RoundWeight(row.Qty)
            : Math.Round(row.Qty, 0);
        var q = row.WeighedLine
            ? JsonNumericReader.AdjustWeighedQuantity(currentQty, direction)
            : currentQty + direction;

        if (q <= 0)
        {
            await DeleteLineAsync(row.ItemId).ConfigureAwait(true);
            return;
        }

        var qtyStr = FormatQuantityForApi(q, row.WeighedLine);
        if (direction > 0 && !string.IsNullOrEmpty(row.ProductId))
        {
            var delta = row.WeighedLine ? (double)JsonNumericReader.WeightStepKg : 1.0;
            if (!StockAvailabilityService.CanAddQuantity(row.ProductId, delta, _cart, _viewingDeferredEntryId))
            {
                ShowNoStockBlocked(row.Title, row.ProductId);
                return;
            }
        }

        SetScanBusy(true);
        try
        {
            if (UseSnapshotEditing)
            {
                LogCartOpDiagnostics(direction > 0 ? "PLUS" : "MINUS", row.ItemId);
                ReceiptSnapshotCartEditor.UpdateLineQuantity(_cart, row.ItemId, q);
                CartMessageText.Text = "";
                AfterDeferredCartMutation();
                return;
            }

            if (OfflineModeHelper.UseLocalOperations || _cart.IsLocalOffline)
            {
                LocalCartService.UpdateLineQuantity(_cart, row.ItemId, q);
                CartMessageText.Text = "";
                RebindCartUi();
                return;
            }

            LogCartOpDiagnostics(direction > 0 ? "PLUS" : "MINUS", row.ItemId);
            var resp = await App.SalesApi
                .PosCartItemPatchAsync(
                    _cart.CartId!,
                    row.ItemId,
                    new Dictionary<string, string> { ["quantity"] = qtyStr })
                .ConfigureAwait(true);
            if (!CartResponseHelper.TryUpdateCartSession(resp, _cart))
                CartInPlaceRecalculator.UpdateLineQuantity(_cart, row.ItemId, q, row.WeighedLine);
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
            LogCartOpDiagnostics("REMOVE", itemId);

            if (UseSnapshotEditing)
            {
                ReceiptSnapshotCartEditor.RemoveLine(_cart, itemId);
                CartMessageText.Text = "";
                AfterDeferredCartMutation();
                return;
            }

            if (OfflineModeHelper.UseLocalOperations || _cart.IsLocalOffline)
            {
                LocalCartService.RemoveLine(_cart, itemId);
                CartMessageText.Text = "";
                RebindCartUi();
                return;
            }

            await App.SalesApi.PosCartItemDeleteAsync(_cart.CartId!, itemId).ConfigureAwait(true);
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

    private static string FormatQuantityForApi(double q, bool weighed) =>
        weighed
            ? JsonNumericReader.FormatWeightForApi(q)
            : Math.Round(q, 0).ToString(CultureInfo.InvariantCulture);

    private static string FormatQtySubline(double qty, bool weighed) =>
        weighed
            ? CartDisplayHelper.FormatWeightQuantity(qty)
            : Math.Round(qty, 0).ToString(CultureInfo.InvariantCulture);

    internal void RebindCartUi()
    {
        CartLines.Clear();
        if (!_cart.HasCart)
        {
            CartTotalAmountText.Text = "0.00";
            if (CartSubtotalText != null) CartSubtotalText.Text = "Промежуточный итог: 0.00 сом";
            if (CartDiscountText != null) CartDiscountText.Text = "";
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

        var root = _cart.Root;
        var hasStockIssues = false;
        foreach (var it in CartDisplayHelper.EnumerateItems(root))
        {
            var iid = CartDisplayHelper.TryItemId(it);
            if (string.IsNullOrEmpty(iid))
                continue;

            var productId = CartDisplayHelper.TryProductId(it) ?? "";
            var weighed = CartDisplayHelper.LineMustWeigh(it);
            var qtyVal = CartDisplayHelper.LineQuantity(it);
            var up = CartDisplayHelper.UnitPrice(it);
            var unit = weighed ? "кг" : "шт";
            var sub = $"{FormatQtySubline(qtyVal, weighed)} {unit} × {CartDisplayHelper.FormatMoney(up)} сом";
            var priceKg = weighed ? $"{CartDisplayHelper.FormatMoney(up)} сом" : "";

            StockLineStatus? stockStatus = null;
            if (!string.IsNullOrEmpty(productId))
                stockStatus = StockAvailabilityService.EvaluateCartLine(productId, qtyVal, _viewingDeferredEntryId);

            var insufficient = stockStatus?.IsInsufficient ?? false;
            if (insufficient)
                hasStockIssues = true;

            // Чтение скидки с позиции (discount_percent / discount_total)
            string? discType = null;
            decimal? discVal = null;

            if (it.TryGetProperty("discount_percent", out var pctEl)
                && JsonNumericReader.TryToDouble(pctEl, out var pct)
                && Math.Abs(pct) > 1e-9)
            {
                discType = "percent";
                discVal = (decimal)pct;
            }
            else if (it.TryGetProperty("discount_total", out var sumEl)
                     && JsonNumericReader.TryToDouble(sumEl, out var sum)
                     && Math.Abs(sum) > 1e-9)
            {
                discType = "sum";
                discVal = (decimal)sum;
            }

            CartLines.Add(new CartLineRow
            {
                ItemId = iid,
                ProductId = productId,
                Qty = qtyVal,
                WeighedLine = weighed,
                Title = CartDisplayHelper.ItemName(it),
                SubLine = sub,
                LineTotal = CartDisplayHelper.LineTotal(it),
                PricePerKgHint = priceKg,
                DiscountType = discType,      // ← Важно!
                DiscountValue = discVal,       // ← Важно!
                InsufficientStock = insufficient,
                StockWarningText = insufficient ? "Товара недостаточно на складе" : "",
                StockAvailableText = stockStatus != null
                    ? $"Доступно: {FormatStockQty(stockStatus.Available)} {unit}"
                    : "",
            });
        }

        if (CartLines.Count == 0 && IsViewingDeferredReceipt)
        {
            TryCloseEmptyDeferredReceipt();
            return;
        }

        var totals = CartTotalsCalculator.Calculate(root);
        if (CartSubtotalText != null)
            CartSubtotalText.Text = $"Промежуточный итог: {totals.SubtotalFormatted} сом";
        if (CartDiscountText != null)
        {
            var discount = totals.LineDiscounts + totals.OrderDiscount;
            CartDiscountText.Text = discount > 1e-6
                ? $"Скидка: −{CartTotalsCalculator.FormatMoney(discount)} сом"
                : "";
        }

        CartTotalAmountText.Text = totals.TotalDueFormatted;
        var canEdit = _cart.CanRefresh || UseSnapshotEditing || _cart.IsLocalOffline;
        RefreshCartButton.IsEnabled = _cart.CanRefresh;
        CheckoutFooterButton.IsEnabled = canEdit && CartLines.Count > 0 && !hasStockIssues;
        ScanBarcodeButton.IsEnabled = canEdit;
        BarcodeBox.IsEnabled = canEdit;
        SyncDiscountFieldsFromCart();
        UpdateShiftBanner();
        UpdateCartStateUi();
        UpdateDeferredCartUi();
        SetScanBusy(_isUiBusy);
    }

    public void ApplyHardwareAndUiPreferences()
    {
        ApplyFullscreenPreference();
        StartScaleMonitoring();
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
            Hide();

            var next = App.GetRequiredService<MainWindow>();
            Application.Current.MainWindow = next;
            next.WindowState = WindowState.Maximized;
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
        // Освобождаем COM-порт весов, пока открыты Настройки: иначе фоновое чтение
        // держит порт и проверка/тест весов в настройках получает «занят».
        _scaleUiTimer?.Stop();
        _scaleUiTimer = null;
        _weightScale.Stop();

        try
        {
            var dlg = new PosSettingsWindow { Owner = this };
            dlg.ShowDialog();
        }
        finally
        {
            // Возврат на кассу — снова захватываем порт для фонового чтения веса.
            StartScaleMonitoring();
            UpdateScaleStatusLine();
        }
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

        foreach (var key in new[] { "email", "full_name", "name", "username" })
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

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(App.ActiveShiftId))
            {
                var shiftResult = ShiftNotClosedDialog.Show(this);

                if (shiftResult == ShiftNotClosedDialogResult.Cancel)
                    return;

                if (shiftResult == ShiftNotClosedDialogResult.CloseShift)
                {
                    try
                    {
                        await CloseShiftAsync().ConfigureAwait(true);
                    }
                    catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
                    {
                        /* отмена фоновых задач при выходе */
                    }
                }
            }

            NavigateToLogin();
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            /* отмена фоновых задач при выходе */
        }
    }

    private void NavigateToLogin()
    {
        try
        {
            FrmKeyboard.KillKeyboard();

            try
            {
                if (!_windowCts.IsCancellationRequested)
                    _windowCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                /* окно уже закрывается */
            }

            App.AuthApi.ClearSession();
            _cart.Clear();
            App.PosCashboxId = null;
            App.PosCashboxDisplayName = null;
            App.ActiveShiftId = null;
            App.IsOfflineBootstrap = false;
            App.OfflineBootstrapMessage = null;
            App.SkipOfflineAutoLogin = true;
            Hide();

            var login = App.GetRequiredService<LoginWindow>();
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
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            /* отмена фоновых задач при закрытии интерфейса — нормальное поведение */
        }
    }

    // ────────── Управление наличными (панель смены) ──────────

    private static readonly string CashHistoryFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NurMarketKassa", "cash_history.json");

    // Показать панель «Внесение»


    // Применить операцию (внесение / изъятие)


    // Отмена операции


    // Сохранить начальный остаток (сумму при открытии смены)


    // История операций с наличными


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

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;

            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }

        return null;
    }

}
