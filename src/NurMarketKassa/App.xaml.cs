using MediatR;
using System.IO;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NurMarketKassa.Configuration;
using NurMarketKassa.Core.Application;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Interfaces;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Api;
using NurMarketKassa.Services.Hardware;
using NurMarketKassa.Ui.Shared;
using NurMarketKassa.ViewModels;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace NurMarketKassa
{
    public partial class App : Application
    {
        private static ScalePrinterAgentService? _agentService;

        public static IHost? AppHost { get; private set; }

        // Временный мост для существующего кода (Фаза P0): постепенная миграция с static App.*
        public static AppSettings Settings { get; private set; } = null!;
        public static NurMarketApiClient Api { get; private set; } = null!;
        public static IAuthApiService AuthApi { get; private set; } = null!;
        public static ICatalogApiService CatalogApi { get; private set; } = null!;
        public static ISalesApiService SalesApi { get; private set; } = null!;
        public static IShiftApiService ShiftApi { get; private set; } = null!;
        public static SyncService OfflineSync { get; private set; } = null!;
        public static CatalogBackgroundSyncService CatalogBackgroundSync { get; private set; } = null!;
        public static MySqlAuditService AuditDb { get; private set; } = null!;
        public static string? CurrentUserId
        {
            get => PosApp.CurrentUserId;
            set => PosApp.CurrentUserId = value;
        }
        public static string? PosCashboxId
        {
            get => PosApp.PosCashboxId;
            set => PosApp.PosCashboxId = value;
        }
        internal static bool ExitWithoutLoginRedirect { get; set; }
        public static string? PosCashboxDisplayName
        {
            get => PosApp.PosCashboxDisplayName;
            set => PosApp.PosCashboxDisplayName = value;
        }
        public static string? ActiveShiftId
        {
            get => PosApp.ActiveShiftId;
            set => PosApp.ActiveShiftId = value;
        }
        public static bool IsOfflineBootstrap
        {
            get => PosApp.IsOfflineBootstrap;
            set => PosApp.IsOfflineBootstrap = value;
        }
        public static string? OfflineBootstrapMessage
        {
            get => PosApp.OfflineBootstrapMessage;
            set => PosApp.OfflineBootstrapMessage = value;
        }
        /// <summary>После явного выхода не выполнять автоматический офлайн-вход на экране логина.</summary>
        public static bool SkipOfflineAutoLogin { get; set; }

        static App()
        {
            EventManager.RegisterClassHandler(
                typeof(UIElement),
                UIElement.PreviewGotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(TouchKeyboard.OnPreviewKeyboardFocus),
                handledEventsToo: true);
        }

        public static T GetRequiredService<T>() where T : notnull =>
            AppHost!.Services.GetRequiredService<T>();

        private static readonly Uri PosDialogThemeUri =
            new("Views/Dialogs/PosDialogTheme.xaml", UriKind.Relative);

        public static void ApplyTheme(bool dark)
        {
            var themeUri = new Uri(
                dark ? "Themes/AppThemeDark.xaml" : "Themes/AppThemeLight.xaml",
                UriKind.Relative);
            var merged = Current.Resources.MergedDictionaries;
            merged.Clear();
            merged.Add(new ResourceDictionary { Source = themeUri });
            merged.Add(new ResourceDictionary { Source = PosDialogThemeUri });
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Settings = AppSettings.Load();
            UserPreferences.LoadFromDiskAndMergeDefaults(Settings);
            ApplyTheme(UserPreferences.Instance.DarkTheme);
            AutostartHelper.SyncFromPreference(UserPreferences.Instance.Autostart);

            AppHost = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(
                        typeof(IPosSessionService).Assembly,
                        typeof(PosCheckoutService).Assembly,
                        typeof(App).Assembly));

                    services.AddSingleton<IAppSession, WpfAppSession>();
                    services.AddSingleton<Ui.Shared.IDispatcher, WpfDispatcher>();
                    services.AddSingleton<ICatalogCacheService, WpfCatalogCacheService>();
                    services.AddSingleton<ILocalAccountsStore, LocalAccountsManager>();
                    services.AddSingleton<IConnectivityService, ConnectivityService>();
                    services.AddSingleton<IOfflineLoginSupport, OfflineLoginSupport>();
                    services.AddSingleton(Settings);

                    if (HardwareModeHelper.UsePhysicalScale())
                        services.AddSingleton<IWeightScaleService, ComWeightScaleService>();
                    else if (HardwareModeHelper.UseDemoHardware(Settings))
                        services.AddSingleton<IWeightScaleService, VirtualWeightScaleService>();
                    else
                        services.AddSingleton<IWeightScaleService, ComWeightScaleService>();

                    if (HardwareModeHelper.UsePhysicalPrinter())
                        services.AddSingleton<IReceiptPrinterService, LptReceiptPrinterService>();
                    else if (HardwareModeHelper.UseDemoHardware(Settings))
                        services.AddSingleton<IReceiptPrinterService, VirtualReceiptPrinterService>();
                    else
                        services.AddSingleton<IReceiptPrinterService, LptReceiptPrinterService>();

                    services.AddSingleton<MySqlSettings>(sp =>
                        sp.GetRequiredService<AppSettings>().MySql);
                    services.AddSingleton<MySqlAuditService>(sp =>
                    {
                        var audit = new MySqlAuditService(sp.GetRequiredService<MySqlSettings>());
                        audit.Initialize();
                        return audit;
                    });
                    services.AddSingleton<PostgreSqlSettings>(sp =>
                        PostgreSqlConnectionStringResolver.ResolveRuntimeSettings(
                            sp.GetRequiredService<AppSettings>().PostgreSql,
                            UserPreferences.Instance));
                    services.AddSingleton<ProductSearchService>();
                    services.AddSingleton<ICartService, CartService>();
                    services.AddSingleton<NurMarketApiClient>();
                    services.AddSingleton<IAuthApiService, AuthApiService>();
                    services.AddSingleton<ICatalogApiService, CatalogApiService>();
                    services.AddSingleton<ISalesApiService, SalesApiService>();
                    services.AddSingleton<IShiftApiService, ShiftApiService>();
                    services.AddSingleton<IUserPrompts, WpfUserPrompts>();
                    services.AddSingleton<IBarcodeInputService, KeyboardWedgeBarcodeService>();
                    services.AddSingleton<IShiftStateService, ShiftStateService>();
                    services.AddSingleton<IShiftOpenCoordinator, WpfShiftOpenCoordinator>();
                    services.AddSingleton<IPosCartGateway, WpfPosCartGateway>();
                    services.AddSingleton<IProductCatalogLookup, WpfProductCatalogLookup>();
                    services.AddSingleton<ScaleWeightProvider>();
                    services.AddSingleton<IScaleWeightProvider>(sp => sp.GetRequiredService<ScaleWeightProvider>());
                    services.AddSingleton<IWeightInputPrompt, WpfWeightInputPrompt>();
                    services.AddSingleton<IMySqlConnectionSettings, WpfMySqlConnectionSettings>();
                    services.AddSingleton<IOfflinePosStateStore, OfflinePosStateStoreAdapter>();
                    services.AddSingleton<ICashShiftService, CashShiftService>();
                    services.AddSingleton<IPosCheckoutService, PosCheckoutService>();
                    services.AddSingleton<IDeferredCartService, DeferredCartService>();
                    services.AddSingleton<CustomerDisplayStateService>();
                    services.AddSingleton<ICustomerDisplayService>(sp => sp.GetRequiredService<CustomerDisplayStateService>());
                    services.AddSingleton<IStockService, Core.Application.StockService>();
                    services.AddSingleton<IInventoryService, Core.Application.InventoryService>();
                    services.AddSingleton<IStockAuditWriter, WpfStockAuditWriter>();
                    services.AddSingleton<ILocalStockProvider, WpfLocalStockProvider>();
                    services.AddSingleton<ILocalStockLedger, Core.Application.LocalStockLedger>();
                    services.AddSingleton<IServerStockGateway, WpfServerStockGateway>();
                    services.AddSingleton<IStockCatalogUpdater, WpfStockCatalogUpdater>();
                    services.AddSingleton(_ => DatabaseService.Instance);
                    services.AddSingleton<AuthService>();
                    services.AddSingleton<IAuthService, PosAuthService>();
                    services.AddSingleton<ISyncConflictResolver, Core.Application.SyncConflictResolver>();
                    services.AddSingleton<SyncService>();
                    services.AddSingleton<IPosBarcodeScanner, Core.Application.PosBarcodeScannerService>();
                    services.AddSingleton<IPosSessionService, PosSessionService>();
                    services.AddTransient<WarehouseViewModel>();
                    services.AddTransient<Views.WarehouseWindow>();
                    services.AddTransient<ViewModels.Catalog.CatalogViewModel>();
                    services.AddTransient<BarcodeScanViewModel>();
                    services.AddTransient<Views.MainWindow>();
                    services.AddTransient<LoginViewModel>();
                    services.AddTransient<Views.LoginWindow>();
                })
                .Build();

            await AppHost.StartAsync();

            UiDispatcherHolder.Current = AppHost.Services.GetRequiredService<Ui.Shared.IDispatcher>();
            CatalogCacheService.CacheUpdated += OnCatalogCacheUpdated;
            CatalogCacheService.ToastRequested += OnCatalogToastRequested;

            if (HardwareModeHelper.UsePhysicalScale())
            {
                var scale = AppHost.Services.GetRequiredService<IWeightScaleService>();
                scale.Start();
                var sp = UserPreferences.Instance;
                PosLogger.Log(
                    $"Физические весы: фоновое чтение запущено при старте приложения ({sp.ScaleComPort} @ {sp.ScaleBaudRate})",
                    "SCALE");
            }

            // Мост: существующие сервисы продолжают работать через static App.*
            Api = AppHost.Services.GetRequiredService<NurMarketApiClient>();
            AuthApi = AppHost.Services.GetRequiredService<IAuthApiService>();
            CatalogApi = AppHost.Services.GetRequiredService<ICatalogApiService>();
            SalesApi = AppHost.Services.GetRequiredService<ISalesApiService>();
            ShiftApi = AppHost.Services.GetRequiredService<IShiftApiService>();
            OfflineSync = AppHost.Services.GetRequiredService<SyncService>();
            CatalogBackgroundSync = new CatalogBackgroundSyncService();
            AuditDb = AppHost.Services.GetRequiredService<MySqlAuditService>();
            PosApp.Settings = Settings;
            PosApp.AuthApi = AuthApi;
            PosApp.CatalogApi = CatalogApi;
            PosApp.SalesApi = SalesApi;
            PosApp.ShiftApi = ShiftApi;
            PosApp.AuditDb = AuditDb;
            AppHost.Services.GetRequiredService<DatabaseService>().EnsureSchema();
            AppHost.Services.GetRequiredService<ILocalAccountsStore>().EnsureSchema();
            CatalogCacheService.EnsureLocalDatabase();
            _ = LocalProductRepository.Instance.WarmUpCacheAsync();
            AppHost.Services.GetRequiredService<IStockService>().Initialize();

            _ = UpdateService.CheckAndPerformUpdateAsync();

            DispatcherUnhandledException += (_, args) =>
            {
                var ex = args.Exception;
                var msg = $"[{DateTime.Now:HH:mm:ss}] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}";

                // В файл
                File.AppendAllText("crash.log", msg + "\n\n");

                // В Output (окно отладки Visual Studio)
                System.Diagnostics.Debug.WriteLine(msg);

                // В PosLogger (если он тоже пишет куда-то)
                PosLogger.Log(msg, "ERROR");

                try
                {
                    PosMessageBox.Show(
                        "Критическая ошибка:\n\n" + ex.Message + "\n\nСтек записан в crash.log и Output",
                        "Nur Market — Касса",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }
                args.Handled = true;
            };

            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (_, args) =>
            {
                if (args.IsAvailable)
                {
                    Dispatcher.InvokeAsync(() => OfflineSync.TriggerSyncNowAsync());
                    Dispatcher.InvokeAsync(() => App.CatalogBackgroundSync.CheckNowAsync());
                }
            };

            base.OnStartup(e);

            var login = GetRequiredService<Views.LoginWindow>();
            login.Show();

            _ = Dispatcher.BeginInvoke(() =>
            {
                OfflineSync.Start();
                CatalogBackgroundSync.Start();
            }, System.Windows.Threading.DispatcherPriority.Background);
            _agentService = new ScalePrinterAgentService();
            _agentService.Start();
        }

        private static void OnCatalogCacheUpdated()
        {
            UiDispatcherHolder.InvokeAsync(() =>
            {
                if (Current.MainWindow is Views.MainWindow mainWindow)
                    mainWindow.UpdateCacheStatus();
            });
        }

        private static void OnCatalogToastRequested(string message, bool isWarning)
        {
            UiDispatcherHolder.InvokeAsync(() =>
            {
                if (Current.MainWindow is Views.MainWindow mainWindow)
                    mainWindow.ShowToast(message, isWarning);
            });
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            CatalogCacheService.CacheUpdated -= OnCatalogCacheUpdated;
            CatalogCacheService.ToastRequested -= OnCatalogToastRequested;

            foreach (Window window in Current.Windows)
                window.Hide();

            OfflineSync.Dispose();
            CatalogBackgroundSync.Dispose();
            AuditDb.Dispose();
            Api.Dispose();

            if (AppHost != null)
            {
                try
                {
                    AppHost.Services.GetService<IWeightScaleService>()?.Stop();
                }
                catch
                {
                    /* ignore */
                }

                await AppHost.StopAsync();
                AppHost.Dispose();
                AppHost = null;
            }

            _agentService?.Dispose();
            base.OnExit(e);
        }
    }
}
