using NurMarketKassa.Configuration;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Hardware;
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
        public static AppSettings Settings { get; private set; } = null!;
        public static NurMarketApiClient Api { get; private set; } = null!;
        public static CartSession Cart { get; } = new();
        public static OfflineSalesSyncService OfflineSync { get; private set; } = null!;
        public static string? CurrentUserId { get; set; }
        public static string? PosCashboxId { get; set; }
        internal static bool ExitWithoutLoginRedirect { get; set; }
        public static string? PosCashboxDisplayName { get; set; }
        public static string? ActiveShiftId { get; set; }

        private HttpClient? _http;

        static App()
        {
            EventManager.RegisterClassHandler(
                typeof(UIElement),
                UIElement.PreviewGotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(TouchKeyboard.OnPreviewKeyboardFocus),
                handledEventsToo: true);
        }

        public static void ApplyTheme(bool dark)
        {
            var uri = new Uri(dark ? "Themes/AppThemeDark.xaml" : "Themes/AppThemeLight.xaml", UriKind.Relative);
            var dict = new ResourceDictionary { Source = uri };
            Current.Resources.MergedDictionaries.Clear();
            Current.Resources.MergedDictionaries.Add(dict);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Инициализация служб
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Settings = AppSettings.Load();
            UserPreferences.LoadFromDiskAndMergeDefaults(Settings);
            ApplyTheme(UserPreferences.Instance.DarkTheme);
            AutostartHelper.SyncFromPreference(UserPreferences.Instance.Autostart);

            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(55) };
            Api = new NurMarketApiClient(_http, Settings);
            OfflineSync = new OfflineSalesSyncService(Api);

            // Фоновая проверка обновлений
            _ = Task.Run(async () =>
            {
                try
                {
                    string? manifestUrl = Settings?.Updates?.ManifestUrl;
                    if (string.IsNullOrWhiteSpace(manifestUrl))
                        manifestUrl = Environment.GetEnvironmentVariable("DESKTOP_MARKET_UPDATE_MANIFEST_URL");

                    if (!string.IsNullOrWhiteSpace(manifestUrl))
                    {
                        var updateService = new UpdateService(manifestUrl);
                        var manifest = await updateService.CheckAsync();
                        if (manifest != null)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (MessageBox.Show($"Доступна новая версия: {manifest.LatestVersion}. Установить?",
                                    "Обновление", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                                {
                                    _ = updateService.DownloadAndRunAsync(manifest.DownloadUrl)
                                        .ContinueWith(t =>
                                        {
                                            if (t.Result)
                                            {
                                                Task.Delay(500).Wait();
                                                Environment.Exit(0);
                                            }
                                        }, TaskScheduler.Default);
                                }
                            });
                        }
                    }
                }
                catch
                {
                    // игнорируем
                }
            });

            // Обработчик ошибок UI
            DispatcherUnhandledException += (_, args) =>
            {
                PosLogger.Log(
                    $"DispatcherUnhandledException: {args.Exception.GetType().FullName}: {args.Exception.Message} | {args.Exception.StackTrace}",
                    "ERROR");
                try
                {
                    MessageBox.Show(
                        "Ошибка интерфейса:\n\n" + args.Exception.Message + "\n\n" + args.Exception.GetType().FullName,
                        "Nur Market — Касса",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }
                args.Handled = true;
            };

            // Автосинхронизация офлайн-чеков
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (_, args) =>
            {
                if (args.IsAvailable)
                    Dispatcher.InvokeAsync(() => OfflineSync.TriggerSyncNowAsync());
            };

            base.OnStartup(e);

            var login = new Views.LoginWindow();
            login.Show();

            _ = Dispatcher.BeginInvoke(() => OfflineSync.Start(), System.Windows.Threading.DispatcherPriority.Background);
            _agentService = new ScalePrinterAgentService();
            _agentService.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Cart.Dispose();
            OfflineSync.Dispose();
            Api.Dispose();
            _http?.Dispose();
            _agentService?.Dispose();
            base.OnExit(e);
        }
    }
}